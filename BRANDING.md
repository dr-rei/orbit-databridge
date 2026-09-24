# Orbit application suite

The desktop tool is intentionally presented as a product in a reusable suite rather than as a one-off technical utility.

## Brand architecture

- **Orbit** is the suite identity for practical tools that help teams understand and operate their systems.
- **DataBridge** is the current product: a safe database comparison and insert-only transfer workspace.
- Future products can reuse the Orbit shell and replace only the product name and descriptor, for example `Orbit · AssetRoom` or `Orbit · SignalDesk`.

## Design direction

The visual language is calm, precise, and operational:

- Deep ink navy creates trust and keeps the workspace grounded.
- Indigo is the action color for comparison and navigation.
- Mint signals a safe/ready state; coral is reserved for destructive or blocked states; amber calls attention to guardrails.
- Rounded white panels and quiet borders make dense technical information easier to scan.
- Copy uses plain verbs: Connect, Compare, Inspect, Plan, Move, Review.
- Safety language is part of the product identity, not an afterthought: every transfer screen repeats the direction and insert-only rule.

## Reuse contract

New Orbit apps should reuse:

1. `src/Dbms.App/Branding/BrandIdentity.cs` as the token source.
2. The dark suite header, left workspace navigation, white panel, and metric-card patterns.
3. The same status hierarchy: user-facing outcome first, technical diagnostics behind an expandable diagnostics area.
4. A product-specific name and descriptor, but never a database-specific brand in the outer shell.
