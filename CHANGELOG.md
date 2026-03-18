# Changelog

## 0.2.0

- Fixed pupils staying large after unshrinking
- Fixed animation speed staying fast after unshrinking
- Fixed grab range not being reduced while shrunken
- Fixed voice pitch getting killed by spewer/hourglass events
- Fixed menu preview not showing big pupils while shrunken
- Fixed non-host players having full grab strength while shrunken (host now enforces for all players)
- Fixed remote players' big pupils not showing in the shop
- Added safety clamp for negative shrink durations from bad config values
- Cleaned up log spam (no more per-second status lines, no more AccessTools warnings)
- Switched to assembly publicizer, removed all reflection from PlayerHandler
- Replaced AccessTools field scanning in ItemHandler with silent GetField lookups
- Updated Thunderstore link in README
- Updated description for better discoverability

## 0.1.0

Initial early access release.
