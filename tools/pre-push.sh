#!/usr/bin/env bash
# git pre-push 钩子 —— 强制「每次交付前本地 Release 构建过」
#
# 由 tools/install-hooks.sh 安装到 .git/hooks/pre-push。
#
# 为什么强制：cs-dw 是 Windows 桌面程序，多个 bug 只有在本机跑起来才暴露
# （托盘注册、边缘唤醒、进程退出回调）。CI 的 runner 环境与本机不同，
# 本地构建过是最低成本的门槛——拦在推送前，而不是等 CI 红了再回头。
#
# 行为：
#   推送前跑 tools/deliver.sh（clean + Release 构建 + 全部自测 + publish + 实跑）
#   任一步失败 → 拒绝推送
#   跳过方式：SKIP_DELIVER=1 git push（仅在紧急情况用，会留痕迹于 CI 日志）
#
# 只在推 main / tag 时强制，推 feature 分支放行——本地开发不必每次全量校验。

set -uo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"

if [[ "${SKIP_DELIVER:-0}" == "1" ]]; then
  echo "⚠ SKIP_DELIVER=1 —— 跳过本地交付校验"
  exit 0
fi

# 判断这次推送的目标：main/master 或 tag 才强制
NEED_CHECK=0
while read -r _local_ref _local_sha _remote_ref _remote_sha; do
  remote_branch="${_remote_ref#refs/heads/}"
  if [[ "$_remote_ref" == refs/tags/* || "$remote_branch" == "main" || "$remote_branch" == "master" ]]; then
    NEED_CHECK=1
  fi
done

if [[ $NEED_CHECK -eq 0 ]]; then
  exit 0
fi

echo "── pre-push：本地 Release 交付校验 ──"
if bash "$REPO_ROOT/tools/deliver.sh"; then
  echo "✓ 交付校验通过，继续推送"
  exit 0
fi

echo
echo "✗ 推送被拒绝：本地 Release 构建/自测未通过"
echo "  修好后再推，或用 SKIP_DELIVER=1 git push 临时跳过（不建议）"
exit 1
