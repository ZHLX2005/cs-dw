#!/usr/bin/env bash
# cs-dw 交付脚本 —— 每次交付前的本地 Release 校验
#
# 用途：把「构建 → 自测 → 发布」这条固定动作固化成一条命令，
#       并挂进 git pre-push 钩子，让「没本地构建过就推不上去」成为机制而非自觉。
#
# 用法：
#   tools/deliver.sh                # 校验（clean + Release 构建 + 全部自测 + publish）
#   tools/deliver.sh --tag v0.1.1   # 校验通过后打 tag 并推送（触发 Release 流水线）
#   tools/deliver.sh --skip-tests   # 只构建不跑自测（不推荐；pre-push 不允许用这个）
#
# 退出码：0 = 全部通过；非 0 = 失败（pre-push 钩子据此拦截推送）

set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="$ROOT/src"
PROJ="$SRC/NxDrawer.csproj"
DIST="$ROOT/dist"

TAG=""
SKIP_TESTS=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --tag) TAG="${2:-}"; shift 2 ;;
    --tag=*) TAG="${1#*=}"; shift ;;
    --skip-tests) SKIP_TESTS=1; shift ;;
    -h|--help) sed -n '2,15p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) echo "未知参数: $1（--help 看用法）"; exit 2 ;;
  esac
done

STEP=0
step() { STEP=$((STEP+1)); echo; echo "── [$STEP] $1 ──"; }
fail() { echo; echo "✗ 交付校验失败：$1"; exit 1; }

echo "cs-dw 交付校验"
echo "仓库根: $ROOT"

# ── 0. 杀掉残留实例 ────────────────────────────────────────────
# 旧实例会锁住 exe，导致 dotnet build 报 MSB3027（文件被占用）。
# 在 Git Bash 里 taskkill /PID 会被当成路径，必须走 cmd //c。
step "清理残留 nx-drawer 实例"
if cmd //c "tasklist | findstr /I nx-drawer.exe" >/dev/null 2>&1; then
  cmd //c "taskkill /IM nx-drawer.exe /F" 2>&1 | head -3
  sleep 1
  echo "已清理"
else
  echo "无残留实例"
fi

# ── 1. 干净构建 Release（警告视为错误）──────────────────────────
step "clean + Release 构建（-warnaserror）"
cd "$SRC" || fail "进不去 $SRC"
dotnet clean -c Release --nologo -v q || fail "dotnet clean 失败"
# -warnaserror：任何新警告都让交付失败。零警告是本项目的基线。
if ! dotnet build -c Release --nologo -warnaserror; then
  fail "Release 构建失败（警告也算失败）"
fi

# ── 2. 自测 ───────────────────────────────────────────────────
if [[ $SKIP_TESTS -eq 0 ]]; then
  step "自测（smoke / launch / edge / tray）"
  for mode in --smoke --launch-test --edge-test --tray-test; do
    echo "→ $mode"
    if ! dotnet run -c Release --no-build -- "$mode"; then
      fail "自测 $mode 失败"
    fi
  done
else
  step "自测已跳过（--skip-tests）"
fi

# ── 3. publish 出发布物 ───────────────────────────────────────
step "publish → $DIST"
rm -rf "$DIST"
if ! dotnet publish "$PROJ" -c Release -o "$DIST" -r win-x64 --self-contained false --nologo; then
  fail "publish 失败"
fi

# ── 4. 校验发布物完整性 ───────────────────────────────────────
step "校验发布物"
missing=""
for f in nx-drawer.exe nx-drawer.dll nx-drawer.deps.json nx-drawer.runtimeconfig.json drawer.json; do
  [[ -f "$DIST/$f" ]] || missing="$missing $f"
done
[[ -d "$DIST/assets" ]] || missing="$missing assets/"
if [[ -n "$missing" ]]; then
  fail "发布物缺少：$missing"
fi
echo "发布物齐备："
ls -1 "$DIST" | sed 's/^/  /'

# ── 5. 实跑发布物（不是构建产物，是真正要发出去的那个）────────
step "实跑发布物"
if ! (cd "$DIST" && ./nx-drawer.exe --smoke); then
  fail "发布物运行失败"
fi

# ── 6. 打 tag（可选）──────────────────────────────────────────
if [[ -n "$TAG" ]]; then
  step "打 tag 并推送：$TAG"
  cd "$ROOT" || fail "进不去仓库根"
  if ! git diff --quiet || ! git diff --cached --quiet; then
    fail "工作区有未提交改动——先提交再打 tag"
  fi
  git tag -a "$TAG" -m "Release $TAG" || fail "打 tag 失败"
  git push origin "$TAG" || fail "推 tag 失败"
  echo "已推送 $TAG，Release 流水线已触发"
  echo "  → https://github.com/ZHLX2005/cs-dw/releases"
fi

echo
echo "✓ 交付校验全部通过"
[[ -n "$TAG" ]] || echo "  提示：加 --tag v0.x.y 可直接打 tag 并推送"
