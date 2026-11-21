# Phase 6 bank-transfer checkout

Apply the `Phase6CheckoutReservations` EF Core migration before starting the updated API. The checkout quote works independently of order placement.

Set `Checkout__BankTransferEnabled=true` and `Checkout__BankTransferInstructions` on the API to enable bank-transfer orders. Use shop-approved payment instructions. Configure pickup and delivery zones in the admin app. Checkout uses NZD, the shop's non-GST settings, and the existing delivery pricing rules.

1. Add an in-stock part to the storefront cart, open Checkout, and review the order. When placing the order, save its reference and private tracking key. The shopper can enter both on Checkout to see status, shipment details, and download an issued invoice.
2. Staff can search Orders and Invoices by the order reference. Match the transfer in the bank statement, open the online draft, and enter the verified bank reference. An authorized staff member's action records the payment and creates the invoice in one transaction.
3. A draft holds stock for 48 hours. After that, the stock can be sold again and the expiry worker cancels the draft. Reconcile a transfer received after expiry with the customer before arranging fulfillment or a refund.

Storefront order placement uses a random request key. Identical retries return the same order. The server stores a hash of the key. The key allows access to the one order until 30 days after its hold expires, so treat it like a password.

The [implementation plan](../Plan/Phase6-Implementation-plan.md) also covers customer accounts, email delivery, and hosted card payments and refunds. Those features are pending.
