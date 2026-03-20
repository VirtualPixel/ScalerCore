# Changelog

## 0.2.0

- Fixed pupils staying large after unshrinking
- Fixed animation speed staying fast after unshrinking
- Fixed grab range not being reduced while shrunken
- Fixed voice pitch getting killed by spewer/hourglass events
- Fixed menu preview not showing big pupils while shrunken
- Fixed non-host players having full grab strength while shrunken (host now enforces for all players)
- Fixed remote players' big pupils not showing in the shop
- Fixed menu preview shrinking for all players when only one was shrunken
- Fixed non-host not seeing big pupils in their menu preview
- Fixed non-host grab strength/range/throw not restoring after unshrinking
- Grab strength is less punishing now (1.5x scale factor, capped at 100% when shrunk)
- Added MinimumStrength and MaximumStrength config options
- Grab range and throw still scale directly with size (no mercy bonus)
- Menu avatar now animates smoothly when shrinking/unshrinking instead of snapping
- Added safety clamp for negative shrink durations from bad config values
- Cleaned up log spam (no more per-second status lines, no more AccessTools warnings)
- Switched to assembly publicizer, removed all reflection from PlayerHandler
- Replaced AccessTools field scanning in ItemHandler with silent GetField lookups
- Updated Thunderstore link in README
- Updated description for better discoverability
- Fixed big pupils overriding expressions while shrunken
- Fixed cart pull distance being affected by host's shrink state for all players
- Removed unused REPOLib dependency
- Cleaned up duplicated grab strength calculation

## 0.1.0

Initial early access release.
