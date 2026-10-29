#!/usr/bin/env bash
# 安装 git 钩子 —— 把「每次交付前本地 Release 构建」变成机制
#
# 用法：bash tools/install-hooks.sh
#
# 装的是 .git/hooks/pre-push：推 main/master 或 tag 时，
# 自动跑 tools/deliver.sh（clean + Release 构建 + 全部自测 + publish + 实跑发布物），
# 任一步失败就拒绝推送。
#
# 钩子不进 git（.git/hooks 默认不跟踪），所以换机器要重新装一次——
# 这就是本脚本存在的原因。

set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HOOKS_DIR="$ROOT/.git/hooks"
TARGET="$HOOKS_DIR/pre-push"

if [[ ! -d "$ROOT/.git" ]]; then
  echo "✗ 不是 git 仓库：$ROOT"
  exit 1
fi

mkdir -p "$HOOKS_DIR"

if [[ -f "$TARGET" ]] && ! grep -q "cs-dw" "$TARGET"; then
  echo "✗ 已存在别的 pre-push 钩子，未覆盖：$TARGET"
  echo "  手动合并后再试。"
  exit 1
fi

cp "$ROOT/tools/pre-push.sh" "$TARGET"
chmod +x "$TARGET"

echo "✓ 已安装 pre-push 钩子：$TARGET"
echo
echo "  推 main/master 或 tag 时会自动执行："
echo "    clean → Release 构建(-warnaserror) → 3 个自测 → publish → 实跑发布物"
echo "  任一步失败即拒绝推送。"
echo
echo "  紧急跳过：SKIP_DELIVER=1 git push"
