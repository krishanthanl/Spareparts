# Phase 6 remaining work

Phase 6 is **paused and incomplete** on the local `codex/Phase-6` branch.

Guest bank-transfer checkout is implemented. Shoppers can get a quote, place an order with a 48-hour stock hold, track shipments, and download an issued invoice. Staff can record a verified transfer and create the invoice in one action. See the [checkout setup and workflow](../docs/phase6-checkout.md).

## Resume here

- [ ] Add customer accounts with email verification, password recovery, profile editing, and order history.
- [ ] Add order and invoice emails with durable retries and email-based guest order access.
- [ ] Implement hosted card checkout in sandbox with verified payment webhooks, reservation expiry, and late-payment reconciliation.
- [ ] Add customer return and refund requests, staff review, and gateway refund processing.
- [ ] Run end-to-end checks for the remaining checkout, payment, email, and refund flows.

See the [Phase 6 implementation plan](Phase6-Implementation-plan.md) for the scope, proposed defaults, and acceptance checks.
