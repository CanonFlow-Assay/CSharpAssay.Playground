# Gilded Rose requirements (upstream)

This scoped copy comes from the pinned upstream revision recorded in
`provenance.json`. Inventory items have a `SellIn` day count and `Quality`.
Normal quality degrades daily and twice as fast after expiry. Quality stays
between 0 and 50, except legendary Sulfuras remains at 80 and never changes.
Aged Brie improves with age. Backstage passes improve faster inside ten and
five days, then become worthless after the concert. The requested Conjured
feature would degrade twice as fast as normal inventory.

The pinned starting implementation does not yet implement the requested
Conjured behavior. This playground preserves that observable legacy behavior
during structural refinement and records the feature gap separately; it does
not smuggle a business change into a refactor.
