# Orkyo account theme

Branding for Keycloak's account console. A child of the stock console
(`parent=keycloak.v3`), supplying logo, palette and typeface only — the console
itself is a React app shipped inside `keycloak-account-ui`, and this theme does
not fork it. `orkyo-account.css` re-points PatternFly's own design tokens rather
than styling components, so a Keycloak upgrade stays an upgrade.

## Things that are easy to get wrong

**`favIcon` needs a leading slash; `logo` must not have one.** `index.ftl` builds
the favicon href as `${resourceUrl}${properties.favIcon}` with no separator (its
own default is `/favicon.svg`), so `img/x.png` collapses to `.../orkyoimg/x.png`.
The masthead logo goes through a path join and takes `img/x.png`.

**The theme name must exist in the image that serves it.** Keycloak logs one line
and silently falls back to the built-in theme — which is how `accountTheme:
keycloak.v2` survived a Keycloak 26 upgrade that removed it. Every realm naming a
theme is now asserted against `serverinfo` before cutover, in
`orkyo-infra/scripts/configure-keycloak-deploy.sh`.

**Realms do not pick this up on their own.** `--import-realm` is first-boot only.
A live realm changes through the convergent write in that same script.

**Resources are per theme type.** The account console cannot read the login
theme's files, so the logo and fonts are duplicated here. `scripts/sync-assets.sh`
owns the logo copy — do not edit it by hand.

## Shipping a change

Anything under `keycloak/**` triggers a Keycloak image build on push to `main`,
and the release promotes that image to the version tags. A release whose tip
commit does not touch `keycloak/**` carries the previous image forward instead,
so a theme change must reach `main` in a run that is allowed to finish.
