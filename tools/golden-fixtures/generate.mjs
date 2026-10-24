// Generates the cross-language pricing golden fixture by running the server's
// own round2() (copied verbatim from server/services/order.js:39-41) over a
// battery of realistic POS inputs. The C# side (tests/Pos.Tests/PricingMathTests.cs)
// re-derives the same lines/totals with decimal math and asserts byte-for-byte match.
function round2(n) {
  return Math.round(n * 100) / 100;
}

function priceLine(unitPrice, modifiersTotal, quantity, taxRate) {
  const lineSubtotal = round2((unitPrice + modifiersTotal) * quantity);
  const taxAmount = taxRate != null ? round2(lineSubtotal * taxRate) : 0;
  const lineTotal = round2(lineSubtotal + taxAmount);
  return { unitPrice, modifiersTotal, quantity, taxRate, lineSubtotal, taxAmount, lineTotal };
}

function computeTotals(lines, discounts) {
  const subtotal = round2(lines.reduce((s, l) => s + l.lineSubtotal, 0));
  const taxTotal = round2(lines.reduce((s, l) => s + l.taxAmount, 0));
  const discountTotal = round2(discounts.reduce((s, a) => s + a, 0));
  const total = round2(subtotal - discountTotal + taxTotal);
  return { subtotal, discountTotal, taxTotal, total };
}

const unitPrices = [0, 0.5, 1, 2.99, 9.99, 10, 12.5, 19.99, 49.5, 99.99, 100, 149.99, 250, 349.75, 999.99, 1234.56];
const modifiers = [0, 0.5, 1.25, -1, 2, 3.75];
const quantities = [1, 2, 3, 5, 7, 10, 23];
const taxRates = [null, 0, 0.08, 0.1, 0.16, 0.175];

const lineCases = [];
let i = 0;
for (const unitPrice of unitPrices) {
  for (const modifiersTotal of modifiers) {
    for (const quantity of quantities) {
      for (const taxRate of taxRates) {
        // Keep the fixture file to a manageable size while still covering the
        // cross product's edges: every case through the first ~900, then a
        // deterministic 1-in-5 sample of the rest.
        i++;
        if (i > 900 && i % 5 !== 0) continue;
        lineCases.push(priceLine(unitPrice, modifiersTotal, quantity, taxRate));
      }
    }
  }
}

// A handful of hand-picked edge cases: zero quantity-equivalent amounts,
// a classic float-tie price, and large totals.
const extraLines = [
  priceLine(1.005, 0, 1, null),
  priceLine(0.1, 0.2, 1, 0.1),
  priceLine(19.99, 0, 1, 0.16),
  priceLine(33.33, 0, 3, 0.16),
  priceLine(10000.5, 0, 1, 0.1575),
];
lineCases.push(...extraLines);

// Order-level cases: a handful of multi-line carts with one or more discounts.
const orderCases = [
  { lines: [priceLine(9.99, 0, 2, 0.16), priceLine(2.5, 0.5, 3, 0.16)], discounts: [1] },
  { lines: [priceLine(49.5, 0, 1, 0.08), priceLine(12.5, 0, 4, 0.08), priceLine(3.33, 0, 3, null)], discounts: [5, 2.75] },
  { lines: [priceLine(100, 0, 1, 0.16)], discounts: [16] },
  { lines: [priceLine(19.99, 0, 5, 0.1)], discounts: [] },
  { lines: [priceLine(7.49, 1, 6, 0.175), priceLine(250, 0, 1, 0.175)], discounts: [round2(((7.49 + 1) * 6 * 1.175 + 250 * 1.175) * 0.1)] },
].map(({ lines, discounts }) => ({ lines, discounts, ...computeTotals(lines, discounts) }));

const fixture = { lineCases, orderCases };
process.stdout.write(JSON.stringify(fixture, null, 2) + '\n');
