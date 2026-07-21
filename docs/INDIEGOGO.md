# Indiegogo demo strategy

The campaign should sell one sentence:

> **Speak an order, switch to any ship, and watch your fleet carry out the plan.**

## What the demo must prove

1. Voice control works locally and responds quickly.
2. AI pilots feel competent rather than like remote-controlled cursors.
3. Switching ships changes moment-to-moment play.
4. A tactical order creates a visible, satisfying battlefield consequence.
5. The open-source architecture is real, documented, and contributor-friendly.

## First five campaign visuals

1. Unobstructed battle-gameplay screenshot
2. Voice order changing an intercept trajectory
3. Instant switch from flagship to frigate
4. Local voice → validated order → deterministic pilot diagram
5. Four ship roles and abilities

Use only current, unedited in-engine captures for gameplay imagery. Caption
each image with its mission and what the player is doing so prospective backers
can distinguish playable features from future campaign goals.

## Gameplay trailer

The reproducible 30-second trailer is rendered by the game itself at 1920×1080
and contains its original procedural soundtrack and combat cues. It shows, in
order: a natural-language flagship attack, an instant ship switch, two tactical
abilities, full-fleet combat, the 24-mission campaign, and hosted co-op/PvP.

Run the **Gameplay trailer capture** workflow and download its MP4 artifact.
The workflow rejects the export unless it has 1080p H.264 video, a 29+ fps
frame rate, stereo AAC audio in a campaign-ready loudness range, and the expected duration. The trailer is
deterministic promotional capture; do not describe its scripted command entry
as measured microphone latency. Record and publish a separate hardware demo
before making a voice-latency claim.

## Campaign promise discipline

- Fund a tightly scoped vertical slice before promising a galaxy-sized game.
- Show latency, hardware requirements, and local-model setup honestly.
- Separate implemented features, funded milestones, and long-term ideas.
- Publish the repository before launch so “open source” is independently
  verifiable.
- Use stretch goals only for features that do not threaten the core demo.

The canonical implementation matrix is [CAMPAIGN_SCOPE.md](CAMPAIGN_SCOPE.md).
Campaign copy must be updated whenever that matrix changes.
