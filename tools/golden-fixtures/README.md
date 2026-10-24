# Golden pricing fixtures

`generate.mjs` is `round2()` and the line/total formulas copied verbatim from
`server/services/order.js`, run over a battery of realistic POS inputs (and a few
float-tie edge cases). Its output is `tests/Pos.Tests/Fixtures/pricing-golden.json`,
which `PricingMathTests` in the C# test project replays against
`Pos.Core.Pricing.PricingMath` to prove the decimal port agrees with the server's
float math to the cent — see `Pos.Core/Pricing/Money.cs` for why that's expected to hold.

Regenerate after any change to the pricing math in `order.js`:

```sh
node generate.mjs > ../../tests/Pos.Tests/Fixtures/pricing-golden.json
```

If a new case disagrees, that's real drift to investigate — not a fixture to adjust
without understanding why first.
