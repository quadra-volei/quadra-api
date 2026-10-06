#!/usr/bin/env bash
#
# pre-edit-scope-check.sh
# Optional hook: runs before Edit/Write during agent execution.
# Warns (does not block) when an edit happens in a file outside the expected modules.
#
# To enable, add to .claude/settings.json:
#
# {
#   "hooks": {
#     "PreToolUse": [
#       {
#         "matcher": "Edit|Write",
#         "hooks": [
#           { "type": "command", "command": ".claude/hooks/pre-edit-scope-check.sh" }
#         ]
#       }
#     ]
#   }
# }

# Read the hook's JSON input (path of the file being edited)
input=$(cat)
file_path=$(echo "$input" | jq -r '.tool_input.file_path // .tool_input.path // empty')

if [ -z "$file_path" ]; then
  exit 0
fi

# Allowed paths
case "$file_path" in
  */src/Quadra.*|*/tests/Quadra.*|*/docs/specs/*|*/CLAUDE.md|*/docs/SCOPE.md)
    exit 0
    ;;
  */.claude/*)
    # Agent configuration edit — warn but don't block
    echo "ℹ️  Notice: editing agent configuration. Confirm this is intentional." >&2
    exit 0
    ;;
  *)
    echo "⚠️  Warning: edit on $file_path is outside expected paths (src/, tests/, docs/specs/)." >&2
    echo "    If intentional, proceed. If not, stop and review the spec." >&2
    exit 0  # exit 0 = warn but don't block. exit 2 would block.
    ;;
esac
