# nopCommerce payment plugin

The existing Lunixi form opens **on the merchant website by default**. Hosted checkout remains optional and uses only the validated backend `paymentPageUrl`. Its opaque token can differ from the embedded token; the module never builds this URL from that token. Missing or legacy URLs keep embedded checkout available; hosted checkout stays on hold/reconciliation without a new intent or key. Existing attempts keep their saved mode, request body, profile and key. The official form/panel controls available methods and multi-card options.

Lunixi Payments 1.0.1 implements nopCommerce's native `IPaymentMethod` contract. It targets the **4.90 API line**, with build and native database verification against **4.90.8** and .NET 9. It uses the shared [.NET SDK](https://docs.lunixi.com/en/sdks/dotnet) for request signing, authentication, payment transport and webhook verification.

Source: [Lunixi-Payment/lunixi-nopcommerce](https://github.com/Lunixi-Payment/lunixi-nopcommerce). Source availability does not imply a nopCommerce Marketplace listing or NuGet.org publication.

## Build and install

Merchants install the completed ZIP; no package restore or developer SDK is required on the store. Maintainers building from source need .NET 9 and Python 3.10+:

```bash
python3 scripts/bootstrap.py
python3 scripts/build.py --restore
```

Bootstrap fetches checksum-pinned official nopCommerce 4.90.8 references. A trusted existing NoSource directory can be supplied with `--nop-runtime`. The default shared SDK is `Lunixi.Sdk` 1.0.1 from NuGet.org, restored from the reviewed package lock in an isolated cache. The package must exist before this release build passes. Explicit `--sdk-source` or `--sdk-package` inputs are for reviewed development acceptance; they do not establish registry publication and do not replace the default package lock.

Output: `artifacts/Lunixi.Payments.nopCommerce.4.90-1.0.1.zip` with a SHA-256 file. It contains one native `Payments.Lunixi` directory, the plugin, views/assets and a private SDK vendor context. Platform assemblies are not redistributed.

Upload from **Configuration → Local plugins → Upload plugin or theme**, restart as requested by nopCommerce, then install **Lunixi Payments**. Finally activate it under **Configuration → Payment methods**.

## Server key and merchant settings

Before enabling payments, generate a random 32-byte server key, encode it in base64 and configure `LUNIXI_NOP_ENCRYPTION_KEY` on every application instance. Back it up outside the database and source repository. Replacing or losing it prevents decryption of historical credentials.

| Setting | Purpose |
| --- | --- |
| Merchant ID | Expected merchant identity in authenticated payment records |
| Merchant SECRET key identifier | Server API key; determines TEST/LIVE |
| Ed25519 PKCS#8 PEM private key | Signing; stays server-side |
| Webhook signing secret | Raw-body v2 webhook verification |
| Optional hosted checkout | Disabled by default: embed the existing form on this site. Enable to use the validated backend paymentPageUrl. |
| Authorize now, capture later | Hold funds until a native capture operation |

Secret fields are blank when settings are displayed; leave them blank to preserve values. Secrets and browser tokens are encrypted. Changed credentials create immutable profiles so previous payments can still reconcile and refund. Changing only display/authorization options does not create a new credential profile. Existing attempts retain their original request body and display mode.

The gateway origin is fixed to `https://api-gateway.lunixi.com`; TEST/LIVE comes from the API key. Start with TEST credentials on a staging store. See [Authentication](https://docs.lunixi.com/en/getting-started/authentication) and [Testing](https://docs.lunixi.com/en/getting-started/testing).

## Hosted and embedded forms

Hosted mode sends the buyer to `paymentPageUrl`. Embedded mode loads the fixed official `https://cdn.lunixi.com/lunixi-checkout.min.js` bundle and mounts the existing form. The plugin does not recreate card fields, render gateway HTML or collect raw card data.

Available methods, installments, insurance and split eligibility are managed in the **Lunixi panel**. There are no duplicate method filters in the plugin. Buyer/address prefill comes from trusted native order records; incomplete optional addresses are omitted rather than fabricated. The native customer language selects Turkish or English form locale.

Use HTTPS with trusted proxy headers configured. For embedded checkout, allow the official SDK and its required network/frame origins in your CSP. Customer and store ownership are checked before a token is displayed. Browser return pages only show that verification is pending.

## Webhook and reconciliation

Register the displayed **HTTPS webhook URL** in the Lunixi panel for the correct environment and payment event subscriptions. It has the form `https://shop.example/lunixi/webhook/<profile-id>`. Keep previous endpoint/credential profiles available through refund and reconciliation retention. Rotated endpoint secrets can deliver events for older signing profiles belonging to the same merchant; deduplication follows the bound payment attempt.

The receiver verifies exact raw bytes, signature rotation, signed event/timestamp binding and freshness. It finds only a locally known merchant payment, then performs authenticated payment reads matching merchant ID, payment ID, order reference, currency and amount. A signature or an HTTP 200 alone cannot mark an order paid. Native order writes and inbox completion commit in the same database transaction, with per-order row locking and durable duplicate/conflict detection. → [Webhooks](https://docs.lunixi.com/en/getting-started/webhooks)

The scheduled task performs read-only reconciliation without starving newer orders. Unknown outcomes preserve the original key. A new checkout attempt is allowed only after an authenticated terminal failure without captured funds; token expiry is not permission to create another charge.

## Native capture, refunds and settlement

Use the native order payment actions. Capture/refund/void calls require order-edit capability and retain native admin CSRF gates. Operations are persisted before gateway calls and use stable keys. Complete authenticated settlement balances must confirm the requested effect before a native result reports success.

| Gateway evidence | Native behavior |
| --- | --- |
| Fully captured, matching identity and balances | Paid through native order processing |
| Authorized without capture | Authorized/on hold |
| Confirmed partial/full refund | Native refund state and balances |
| Pending, partial capture, ambiguous or reconciliation-required | Remains pending/review; no fulfillment |
| Complete split parent with balanced aggregate capture and no compensation | May complete the parent order |
| Captured split leg or promissory-note issuance alone | Does not authorize automatic fulfillment |

Settlement uses the store's **primary currency** and integer minor units. Approved fees above the base basket require the persisted order fingerprint echoed in authenticated metadata. Gross charges/refunds are retained separately, so the base basket is not inflated twice. The final native basket refund includes any remaining authenticated installment fee. Product-specific insurance/fulfillment policies still need the corresponding merchant/form capability and suitable order context.

> Split source support does not assert gateway rollout or merchant activation. Pending and reconciliation states never authorize delivery. Promissory-note issuance is not cash collection and needs an explicit merchant fulfillment policy.

nopCommerce applies its financial result after the payment method returns. The plugin reserves this handoff, blocks webhook races and acknowledges native order events/response completion. A process crash during that small handoff can require operator review; the saved operation blocks blind retries. Check native state and authenticated gateway evidence before resolving it.

## Retention and compatibility limits

Uninstall disables the task and preserves financial attempts, operations, inbox, settings and encrypted profiles. Keep the server key and required merchant credentials until refund/dispute retention ends. Recurring payments are not implemented in 1.0.1. Older nopCommerce lines and 5.00 beta are not claimed compatible.

Native database acceptance uses real MariaDB 11/InnoDB and official mappings, repositories, `OrderService` and `OrderProcessingService`. Ambient transaction enlistment is required; MySQL financial/order/note/queued-email tables must be InnoDB and are checked. SQL Server/PostgreSQL dialects are implemented but local native acceptance has not been executed for them; validate those deployments in staging. External third-party event consumers must queue effects durably rather than acting on an uncommitted transaction.

The repository's verification record lists actual commands and limits. Tests include the shared SDK, private assembly/native-crypto loading, native lifecycle, concurrency/replay, rollback, encrypted storage, capabilities, amount/fee/split evidence and unknown outcomes. No live charge is made, and source publication does not imply marketplace approval.

`FAILED` with zero capture alone does not prove definitive failure: public status does not expose outcome certainty. It remains review and cannot rotate an attempt key. Only verified `VOIDED`/`EXPIRED` with zero settlement may permit a new attempt.

The canonical browser callback is POST. A separate anonymous, bounded adapter ignores every submitted field and performs only a fixed same-origin 303 redirect. The resulting HTTPS GET restores native customer/store ownership checks before displaying any order link. Neither stage performs gateway reconciliation or treats callback fields as payment proof.
