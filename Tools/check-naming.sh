#!/usr/bin/env bash
# -----------------------------------------------------------------------------
# Enforces the naming contract recorded in Documentation/Naming.md.
#
# No retired word of the earlier working title may remain in the repository as
# an identifier, a display string, a file name or a document word.
#
# Two files necessarily name those words - the contract itself and this script -
# and are skipped. The archival plan document is skipped for the same reason.
# Engine- and shell-builtin tokens are exempted explicitly and narrowly:
#   * `window/stretch/aspect`      - a Godot project setting key
#   * `echo` in .sh/.yml/.yaml     - the shell builtin
#   * `"echo":` in project.godot / scenes/*.tscn - the serialised InputEventKey property
#   * `shadow_enabled` and friends - shadow as lighting is allowed; only the
#                                    retired *systems* are banned, by name
#
# Exit 0 = clean. Exit 1 = a retired word is present.
# -----------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$ROOT"

SKIP=(':!Tools/check-naming.sh' ':!Documentation/Naming.md' ':!Documentation/Plan-v2.md')

failures=0

fail_with() {
    echo "check-naming: FAIL - $1" >&2
    shift
    printf '%s\n' "$@" >&2
    failures=1
}

scan() {
    local label="$1"
    local pattern="$2"
    shift 2

    local hits
    hits="$(git grep -n -i -E "$pattern" -- . "${SKIP[@]}" "$@" || true)"
    if [ -n "$hits" ]; then
        fail_with "$label" "$hits"
    fi
}

# Retired words. One rule per concept so a failure message explains itself.
scan "the retired product name"              'shadowbound'
scan "a retired power-system word"           'shadow[ _-]?power|damagetype\.shadow'
scan "the retired dark-entity name"          'umbra'
scan "the retired role name"                 'warden'
scan "a retired loot-system word"            '\bmemor(y|ies)\b'
scan "a retired character-system word"       '\bflaws?\b|\bdomains?\b|\bcorruption\b'
scan "retired rank words"                    '\bawakened\b|\bascended\b|\bsacred\b|\bdivine\b'
scan "retired setting words"                 '\bnightmare\b|dream realm'

# `echo` needs two exemptions: the shell builtin (scripts and workflows) and the
# serialised Godot InputEventKey property.
echo_hits="$(git grep -n -i -E '\becho(es)?\b' -- . "${SKIP[@]}" ':!*.sh' ':!*.yml' ':!*.yaml' || true)"
if [ -n "$echo_hits" ]; then
    echo_hits="$(printf '%s\n' "$echo_hits" | grep -v '"echo":' || true)"
fi
if [ -n "$echo_hits" ]; then
    fail_with "the retired captive-enemy word" "$echo_hits"
fi

# `aspect` is banned as a system name; the Godot stretch setting key is exempt.
aspect_hits="$(git grep -n -i -E '\baspects?\b' -- . "${SKIP[@]}" || true)"
if [ -n "$aspect_hits" ]; then
    aspect_hits="$(printf '%s\n' "$aspect_hits" | grep -v 'stretch/aspect' || true)"
fi
if [ -n "$aspect_hits" ]; then
    fail_with "the retired system word 'aspect'" "$aspect_hits"
fi

# File and directory names are part of the contract too.
name_hits="$(git ls-files | grep -i -E 'shadowbound|shadow[_-]?power|umbra|warden|memory' || true)"
if [ -n "$name_hits" ]; then
    fail_with "a retired word is used in a file name" "$name_hits"
fi

if [ "$failures" -ne 0 ]; then
    echo "" >&2
    echo "The naming contract lives in Documentation/Naming.md." >&2
    exit 1
fi

echo "check-naming: OK (no retired identifier remains)"
