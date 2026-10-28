using System.Diagnostics;
using System.IO;

namespace NxDrawer;

// ---- 子进程统一管理 ----
// 每个卡片点击 → 按配置启动（cmd /c <cmd>），登记进表；
// 同一应用再点 = 再开一个实例（计数 +1）；停止 = taskkill /T /F 杀整棵树
//（shim → node → 子命令链，必须 /T 才能带走 node 孙进程）。
// 进程退出（无论正常/被杀）→ Exited 回调驱动 UI 计数刷新。

public sealed class ProcessEntry
{
    public required string Group { get; init; }
    public required Process Proc { get; init; }
    public DateTime StartedAt { get; } = DateTime.Now;
}

public sealed class ProcessManager : IDisposable
{
    private readonly List<ProcessEntry> _list = new();
    private readonly object _lock = new();

    /// <summary>key = group 名，value = 存活实例数</summary>
    public IReadOnlyDictionary<string, int> Counts
    {
        get
        {
            lock (_lock)
            {
                return _list.GroupBy(e => e.Group).ToDictionary(g => g.Key, g => g.Count());
            }
        }
    }

    /// <summary>任一组有进程退出时触发（UI 刷新计数用）</summary>
    public event Action? Changed;

    public bool IsRunning(string group)
    {
        lock (_lock) return _list.Any(e => e.Group == group);
    }

    public int CountOf(string group)
    {
        lock (_lock) return _list.Count(e => e.Group == group);
    }

    /// <summary>
    /// 启动一个应用。cmd 走 <c>cmd /c</c> 以支持 nx-xx shim（.cmd）与任意 CLI；
    /// 返回 false 表示命令为空或启动失败（异常吞掉，UI 上以计数不变化体现）。
    /// </summary>
    public bool Start(AppItemConfig app)
    {
        var group = string.IsNullOrWhiteSpace(app.Group) ? app.Name : app.Group;
        if (!string.IsNullOrWhiteSpace(app.Url))
        {
            // url 类型：交给系统默认浏览器，不进管理表
            try { Process.Start(new ProcessStartInfo(app.Url) { UseShellExecute = true }); return true; }
            catch { return false; }
        }
        if (string.IsNullOrWhiteSpace(app.Cmd)) return false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {app.Cmd}",
                UseShellExecute = false,
                CreateNoWindow = true,          // 不弹黑框，输出可选重定向
                WorkingDirectory = string.IsNullOrWhiteSpace(app.Cwd)
                    ? Directory.GetCurrentDirectory()
                    : app.Cwd,
            };
            var p = Process.Start(psi);
            if (p is null) return false;

            var entry = new ProcessEntry { Group = group, Proc = p };
            // Exited 需要 EnableRaisingEvents；退出回调里顺手清表
            p.EnableRaisingEvents = true;
            p.Exited += (_, _) =>
            {
                lock (_lock) _list.RemoveAll(e => e.Proc.Id == p.Id);
                Changed?.Invoke();
            };
            lock (_lock) _list.Add(entry);
            Changed?.Invoke();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>停掉某组全部实例：taskkill /T /F 连带 node 孙进程。</summary>
    public int StopGroup(string group)
    {
        ProcessEntry[] targets;
        lock (_lock) targets = _list.Where(e => e.Group == group).ToArray();
        int killed = 0;
        foreach (var e in targets)
        {
            try
            {
                using var killer = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = $"/PID {e.Proc.Id} /T /F",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                });
                killer?.WaitForExit(3000);
                killed++;
            }
            catch { /* 进程可能已退出 */ }
        }
        lock (_lock) _list.RemoveAll(e => e.Group == group);
        if (killed > 0) Changed?.Invoke();
        return killed;
    }

    /// <summary>程序退出时全量清场，避免孤儿 node 进程常驻。</summary>
    public void StopAll()
    {
        lock (_lock)
        {
            foreach (var g in _list.Select(e => e.Group).Distinct().ToArray())
                StopGroup(g);
        }
    }

    public void Dispose() => StopAll();
}
