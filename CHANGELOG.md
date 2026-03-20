# Changelog

## 0.2.0

### Bug fixes
- Pupils no longer stay huge after unshrinking
- Animation speed resets properly on unshrink
- Grab range actually scales down while shrunken now
- Voice pitch no longer gets nuked by spewer/hourglass events
- Menu preview shows big pupils while shrunken
- Host now enforces grab stats for all shrunken players, not just local
- Remote players see big pupils in the shop
- Menu preview only shrinks for the shrunken player, not everyone
- Non-host sees their own big pupils in menu preview
- Non-host grab strength/range/throw restores properly after unshrinking
- Big pupils yield to expressions while shrunken (no more bleeding through eyelids)
- Cart pull distance no longer leaks the host's shrink state to other players
- Shrunken enemies deal scaled damage across the board (mace swings, tumble impacts, instakills)
- Enemies like Trudge whose mace has `playerKill` no longer instakill when shrunken
- Damage scaling works even when the HurtCollider doesn't have `enemyHost` set

### Balance
- Grab strength less punishing (1.5x scale factor, capped at 100% when shrunk)
- Added MinimumStrength and MaximumStrength config options
- Grab range and throw scale directly with size (no mercy bonus)
- Enemy damage scales by shrink factor directly (was a flat 0.1x)
- Enemy bonk immunity down from 5s to 3s
- Items stay shrunk indefinitely (was 300s)

### Improvements
- Menu avatar animates smoothly when shrinking/unshrinking instead of snapping
- Negative shrink durations from bad configs get clamped to 0
- Version auto-stamped from csproj via BuildInfo

### Internal
- Assembly publicizer replaces all reflection in PlayerHandler
- ItemHandler uses standard GetField instead of AccessTools
- All enemy-to-player damage scaling lives in one patch now (KnockbackPatch)
- Deduplicated grab strength formula into GetGrabFactors helper
- Noisy item field logs downgraded to LogDebug
- Dropped REPOLib dependency (wasn't actually used)
- Updated Thunderstore description

## 0.1.0

Initial early access release.
