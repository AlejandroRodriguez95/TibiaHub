# Petrol & Lantern frontend

The visual reference is [the landing mockup](mockups/landing-petrol-lantern.png). Apply these rules to every Angular page, including account and admin screens. The implementation is responsive HTML and CSS, not the mockup embedded as an image.

## Color and surfaces

Use the CSS custom properties in `client/tibiaintel-angular/src/styles.scss` as the source of truth. Background `#131d20`, panels `#202d30`, raised inputs `#26363a`, borders `#405459`, primary text `#e2e5df`, secondary text `#b0bdbe`, sea glass links `#91b8b0`, lantern amber actions `#d3af78`. Primary actions use dark text on amber. Use red for errors and offline VIP presence, and the `--positive` green token for online VIP presence. Pair presence colors with text labels. Do not use status colors decoratively or claim online status without fresh data.

Keep surfaces matte, with fine borders and 6–10px corners. Avoid neon, luminous shadows, glass effects, bright white backgrounds, ornate frames, and large fantasy scenery. Sparse compass/route line art may decorate the hero without carrying information.

## Type and spacing

Use Georgia for the brand and headings; use the system sans-serif stack for controls, navigation, labels, and body copy. No external font dependency. Large editorial hero type, restrained uppercase eyebrows, body line-height around 1.6. Limit paragraph width for reading. Use an 8px spacing rhythm, a centered 1240px maximum content width, and generous space between sections.

## Layout and content

Home: shared navigation, split introduction and clearly labeled example profile, dedicated search band, three feature cards, shared footer. Character lookup and account/admin pages use the same panel, form, and data-list vocabulary. Show only supported features; example profiles are illustrative and must be labeled. Do not invent live counters, online states, testimonials, or unsupported product promises.

At narrow widths stack the hero, search controls, and cards. Keep all inputs within their grid tracks. Navigation wraps without horizontal scrolling. Avoid fixed panel heights; long names and emails must wrap.

## Interaction and accessibility

Use real links for navigation, buttons for actions, explicit form labels, a skip link, visible keyboard focus, active-route indication, and at least 44px control targets. Primary action is amber; secondary navigation is sea glass. Disabled controls remain legible. Announce loading and errors. Respect reduced-motion preferences. Decorative SVGs are hidden from assistive technology.

## Routes and maintenance

`/` is the landing page; `/characters` is lookup and accepts a `name` query parameter. `/tools/vip` is the private VIP list (anonymous visitors see a login prompt); `/tools/npc-selling` is the NPC catalogue and `/tools/character-scan` is the authenticated scan UI. `/tools` and `/scans` redirect to those pages respectively. Keep login, registration, account preferences, and admin flows intact. Reuse tokens and shared classes instead of adding page-specific color literals. New features should follow this document and update it when the design changes.

## Deferred frontend polish

The user confirmed on 2026-10-23 that VIP functionality works as intended, but the
current frontend is not clear enough and needs revision. A frontend polish pass is
planned across all features, including character lookup, NPC selling/market values,
character scans, VIP, and account/admin screens. This work is deferred; no replacement
layout or new visual direction has been approved yet. Petrol & Lantern remains the
current design reference.

Review the following during that pass:

- Make the primary action, controls, and results easy to identify on each page.
- Improve labels, grouping, visual hierarchy, spacing, and action placement consistently across features.
- For VIP, clarify character versus group status, multi-group membership editing, expand/collapse controls, and removal from a group versus removal from the whole list.
- Make loading, empty, error, login-required, reconnecting, and unknown/stale-data states understandable.
- Check keyboard use, narrow screens, and long lists while retaining text labels alongside status colors.

These are review areas, not a finalized redesign. Preserve existing feature behavior
and API contracts unless a separate functional change is agreed. Obtain user feedback
on the revised interface before treating frontend polish as complete.
