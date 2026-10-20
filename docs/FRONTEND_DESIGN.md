# Petrol & Lantern frontend

The visual reference is [the landing mockup](mockups/landing-petrol-lantern.png). Apply these rules to every Angular page, including account and admin screens. The implementation is responsive HTML and CSS, not the mockup embedded as an image.

## Color and surfaces

Use the CSS custom properties in `client/tibiahub-angular/src/styles.scss` as the source of truth. Background `#131d20`, panels `#202d30`, raised inputs `#26363a`, borders `#405459`, primary text `#e2e5df`, secondary text `#b0bdbe`, sea glass links `#91b8b0`, lantern amber actions `#d3af78`. Primary actions use dark text on amber. Reserve red for errors; do not use status colors decoratively or claim online status without data.

Keep surfaces matte, with fine borders and 6–10px corners. Avoid neon, luminous shadows, glass effects, bright white backgrounds, ornate frames, and large fantasy scenery. Sparse compass/route line art may decorate the hero without carrying information.

## Type and spacing

Use Georgia for the brand and headings; use the system sans-serif stack for controls, navigation, labels, and body copy. No external font dependency. Large editorial hero type, restrained uppercase eyebrows, body line-height around 1.6. Limit paragraph width for reading. Use an 8px spacing rhythm, a centered 1240px maximum content width, and generous space between sections.

## Layout and content

Home: shared navigation, split introduction and clearly labeled example profile, dedicated search band, three feature cards, shared footer. Character lookup and account/admin pages use the same panel, form, and data-list vocabulary. Show only supported features; example profiles are illustrative and must be labeled. Do not invent live counters, online states, testimonials, or unsupported product promises.

At narrow widths stack the hero, search controls, and cards. Keep all inputs within their grid tracks. Navigation wraps without horizontal scrolling. Avoid fixed panel heights; long names and emails must wrap.

## Interaction and accessibility

Use real links for navigation, buttons for actions, explicit form labels, a skip link, visible keyboard focus, active-route indication, and at least 44px control targets. Primary action is amber; secondary navigation is sea glass. Disabled controls remain legible. Announce loading and errors. Respect reduced-motion preferences. Decorative SVGs are hidden from assistive technology.

## Routes and maintenance

`/` is the landing page; `/characters` is lookup and accepts a `name` query parameter. Keep login, registration, account, and admin flows intact. Reuse tokens and shared classes instead of adding page-specific color literals. New features should follow this document and update it when the design changes.
