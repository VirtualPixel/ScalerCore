#!/usr/bin/env bash
# Runs the ScalerCore grounding probe: builds it into the Developer Gale profile, launches
# R.E.P.O. the way Gale does, waits for the report and prints it.
#
# It never runs while the game is up. Read tools/Probe/README.md before the first run.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GALE_ROOT="$HOME/.local/share/com.kesomannen.gale/repo/profiles"
PROFILE_NAME="Developer"
APP_ID="3241660"

LEVELS="Museum,Manor,Arctic,Wizard"
ENEMIES=""
SCALES=""
BUDGET=""
BUILD_CORE=0
WAIT_SECONDS=720      # 12 minutes
APPEAR_SECONDS=240    # how long the game gets to show up at all
STALL_SECONDS=300     # no new log output for this long means it is wedged
COOLDOWN_SECONDS=120  # a log touched this recently means he only just closed the game

usage() {
    cat <<USAGE
Usage: $(basename "$0") [options]

  --levels  A,B,C    levels to sweep, in order (default: $LEVELS)
  --enemies A,B,C    enemy kinds to summon (default: the probe's own spread)
  --scales  0.5,2    scale factors to test (default: 0.5,0.75,1.5,2,3)
  --budget  600      seconds the probe may spend in game before it quits
  --profile Name     Gale profile to run in (default: $PROFILE_NAME)
  --build-core       rebuild ScalerCore into the profile instead of copying BuildZip
  --wait    720      seconds to wait for the report
  -h, --help         this text
USAGE
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --levels)  LEVELS="$2"; shift 2 ;;
        --enemies) ENEMIES="$2"; shift 2 ;;
        --scales)  SCALES="$2"; shift 2 ;;
        --budget)  BUDGET="$2"; shift 2 ;;
        --profile) PROFILE_NAME="$2"; shift 2 ;;
        --build-core) BUILD_CORE=1; shift ;;
        --wait)    WAIT_SECONDS="$2"; shift 2 ;;
        -h|--help) usage; exit 0 ;;
        *) echo "unknown option: $1" >&2; usage >&2; exit 2 ;;
    esac
done

PROFILE="$GALE_ROOT/$PROFILE_NAME"
BEPINEX="$PROFILE/BepInEx"
PLUGINS="$BEPINEX/plugins"
CONFIG="$BEPINEX/config"
FLAG="$CONFIG/scalercore-probe.flag"
LOG="$BEPINEX/LogOutput.log"
REPORT_SRC="$BEPINEX/scalercore-probe-report.md"
PARTIAL_SRC="$BEPINEX/scalercore-probe-report.partial.md"
REPORT_DST="$REPO_ROOT/tools/Probe/report.md"

die() { echo "probe-grounding: $*" >&2; exit 1; }

# ---- guard: his game comes first -------------------------------------------------------
running=$(ps -eo pid=,args= | grep -v grep | grep -F 'REPO.exe' || true)
if [[ -n "$running" ]]; then
    echo "probe-grounding: REPO.exe is running. Refusing to launch a probe on top of a live game." >&2
    echo "$running" | sed 's/^/  /' >&2
    echo "Close the game, wait for it to fully exit, then run this again." >&2
    exit 1
fi

# A log touched seconds ago means he just quit and is probably restarting into it.
for other_log in "$GALE_ROOT"/*/BepInEx/LogOutput.log; do
    [[ -f "$other_log" ]] || continue
    age=$(( $(date +%s) - $(stat -c %Y "$other_log") ))
    if (( age < COOLDOWN_SECONDS )); then
        die "the $(basename "$(dirname "$(dirname "$other_log")")") profile was in a game ${age}s ago. Give it $COOLDOWN_SECONDS s in case that game is coming back, then run this again."
    fi
done

[[ -d "$PROFILE" ]] || die "no Gale profile at $PROFILE"
[[ -f "$BEPINEX/core/BepInEx.Preloader.dll" ]] || die "no BepInEx preloader in $PROFILE"
command -v steam >/dev/null || die "steam is not on PATH"
command -v dotnet >/dev/null || die "dotnet is not on PATH"

# ---- ScalerCore itself has to be in the profile -----------------------------------------
if (( BUILD_CORE )); then
    echo "probe-grounding: building ScalerCore into $PROFILE_NAME"
    dotnet build "$REPO_ROOT/ScalerCore.csproj" -c Release -p:GalePluginsDir="$PLUGINS" \
        || die "ScalerCore did not build"
else
    staged="$REPO_ROOT/BuildZip/ScalerCore/ScalerCore.dll"
    [[ -f "$staged" ]] || die "no $staged. Build ScalerCore first, or pass --build-core."
    mkdir -p "$PLUGINS/ScalerCore"
    cp -f "$staged" "$PLUGINS/ScalerCore/ScalerCore.dll" || die "could not stage ScalerCore.dll"
    echo "probe-grounding: staged ScalerCore.dll from BuildZip ($(date -r "$staged" '+%Y-%m-%d %H:%M'))"
fi

# ---- build the probe --------------------------------------------------------------------
echo "probe-grounding: building the probe into $PROFILE_NAME"
dotnet build "$REPO_ROOT/tools/Probe/ScalerCoreProbe.csproj" -c Release -p:GalePluginsDir="$PLUGINS" \
    || die "the probe did not build"

# ---- flag file --------------------------------------------------------------------------
mkdir -p "$CONFIG"
{
    echo "$LEVELS"
    [[ -n "$ENEMIES" ]] && echo "enemies=$ENEMIES"
    [[ -n "$SCALES"  ]] && echo "scales=$SCALES"
    [[ -n "$BUDGET"  ]] && echo "budget=$BUDGET"
} > "$FLAG"
echo "probe-grounding: flag written to $FLAG"

rm -f "$REPORT_SRC" "$PARTIAL_SRC"
log_before=0
[[ -f "$LOG" ]] && log_before=$(stat -c %s "$LOG")

cleanup() { rm -f "$FLAG"; }
trap cleanup EXIT

# ---- launch, the way Gale does ----------------------------------------------------------
target="Z:$PROFILE/BepInEx/core/BepInEx.Preloader.dll"
echo "probe-grounding: launching R.E.P.O. on $PROFILE_NAME"
WINEDLLOVERRIDE="winhttp=n,b" steam -applaunch "$APP_ID" \
    --doorstop-enabled true \
    --doorstop-target-assembly "$target" >/dev/null 2>&1 &

# ---- wait: the report, the game dying, a stall, or the clock ----------------------------
started=$(date +%s)
seen_game=0
last_log_size=$log_before
last_log_change=$started
outcome=""

while :; do
    now=$(date +%s)
    elapsed=$(( now - started ))

    if [[ -f "$REPORT_SRC" ]]; then outcome="report"; break; fi

    alive=$(ps -eo pid=,args= | grep -v grep | grep -cF 'REPO.exe' || true)
    if (( alive > 0 )); then
        seen_game=1
    elif (( seen_game == 1 )); then
        # The probe quits the game the moment after it writes; give the write a chance to land.
        for _ in 1 2 3 4 5 6 7 8 9 10; do
            [[ -f "$REPORT_SRC" ]] && break
            sleep 1
        done
        outcome=$([[ -f "$REPORT_SRC" ]] && echo "report" || echo "died")
        break
    elif (( elapsed > APPEAR_SECONDS )); then
        outcome="never-started"; break
    fi

    if [[ -f "$LOG" ]]; then
        size=$(stat -c %s "$LOG")
        if (( size != last_log_size )); then
            last_log_size=$size
            last_log_change=$now
        elif (( seen_game == 1 && now - last_log_change > STALL_SECONDS )); then
            outcome="stalled"; break
        fi
    fi

    if (( elapsed > WAIT_SECONDS )); then outcome="timeout"; break; fi
    sleep 5
done

elapsed=$(( $(date +%s) - started ))
echo "probe-grounding: $outcome after ${elapsed}s"

if [[ -f "$REPORT_SRC" ]]; then
    mkdir -p "$(dirname "$REPORT_DST")"
    cp -f "$REPORT_SRC" "$REPORT_DST"
elif [[ -f "$PARTIAL_SRC" ]]; then
    mkdir -p "$(dirname "$REPORT_DST")"
    cp -f "$PARTIAL_SRC" "$REPORT_DST"
    echo "probe-grounding: only a partial report exists; the run did not finish." >&2
fi

case "$outcome" in
    report) ;;
    died)         echo "probe-grounding: the game exited without finishing. Tail of $LOG:" >&2; tail -40 "$LOG" >&2 ;;
    never-started) echo "probe-grounding: the game never appeared in ${APPEAR_SECONDS}s. Is Steam signed in?" >&2 ;;
    stalled)      echo "probe-grounding: no log output for ${STALL_SECONDS}s. Tail of $LOG:" >&2; tail -40 "$LOG" >&2 ;;
    timeout)
        if (( seen_game == 1 )); then
            echo "probe-grounding: no report after ${WAIT_SECONDS}s; the game is still up, close it." >&2
        else
            echo "probe-grounding: no report after ${WAIT_SECONDS}s and the game never appeared." >&2
        fi ;;
esac

if [[ -f "$REPORT_DST" ]]; then
    echo
    cat "$REPORT_DST"
    echo
    echo "probe-grounding: report saved to $REPORT_DST"
fi

grep -qE '^PASS:' "$REPORT_DST" 2>/dev/null && exit 0
exit 1
