<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="src/Nop.Plugin.Payments.Lunixi/Assets/lunixi-logo-dark.svg"><img src="src/Nop.Plugin.Payments.Lunixi/Assets/lunixi-logo.svg" alt="Lunixi" width="240"></picture></p>


**1.0.2 requires the canonical money-string SDK.** The default build restores the published [Lunixi.Sdk 1.0.1 package from NuGet.org](https://www.nuget.org/packages/Lunixi.Sdk/1.0.1) using the committed package lock and verified registry hash. See [migration](MIGRATION-1.0.md). Historical 0.x assets do not implement this API cut.

# Lunixi Payments for nopCommerce

**Historical merchant ZIP 0.1.2:** [`Lunixi.Payments.nopCommerce.4.90-0.1.2.zip`](https://github.com/Lunixi-Payment/lunixi-nopcommerce/releases/download/v0.1.2/Lunixi.Payments.nopCommerce.4.90-0.1.2.zip) was downloaded and verified on 2026-10-31. It does not implement the 1.0.2 money contract. Install the published merchant ZIP from [GitHub Release v1.0.2](https://github.com/Lunixi-Payment/lunixi-nopcommerce/releases/tag/v1.0.2) with its matching release checksum; GitHub’s automatic source archive is not the installation package.

Public CI ZIP SHA256: `74e5198a455aa0e07f923774ab38182011fa828c50f8476f6202c0f6f7c4386f`.

The existing Lunixi form opens **on the merchant website by default**. Hosted checkout remains optional and uses only the validated backend `paymentPageUrl`. Its opaque token can differ from the embedded token; the module never builds this URL from that token. Missing or legacy URLs keep embedded checkout available; hosted checkout stays on hold/reconciliation without a new intent or key. Existing attempts keep their saved mode, request body, profile and key. The official form/panel controls available methods and multi-card options.

Native payment plugin **1.0.2** for the nopCommerce **4.90 API line**, verified against official **4.90.8** assemblies and .NET 9. The plugin uses the shared [Lunixi .NET SDK](https://github.com/Lunixi-Payment/lunixi-dotnet) for signing, authentication, payment transport and webhook verification. No raw-card form is implemented by the merchant server.

The existing Lunixi form is embedded by default; hosted checkout is optional. Enabled methods, installments, insurance and split availability are controlled in your Lunixi panel; the plugin does not duplicate those switches.

## Compatibility and distribution

- Native manifest: `Payments.Lunixi`, supported version `4.90`; tested with 4.90.8. The 5.00 beta and older API lines are not claimed compatible.
- Native database acceptance uses real MariaDB 11/InnoDB, official mappings/repositories and `OrderProcessingService`. SQL Server/PostgreSQL dialect paths follow the native provider but have not undergone local native database acceptance here; validate them in staging before deployment.
- Merchant installations use the produced ZIP and need no NuGet restore, Composer, Python or .NET SDK. SDK/NSec/libsodium dependencies are bundled under a private assembly-load context with required native assets and licenses.
- GitHub source availability is separate from NuGet.org, nopCommerce Marketplace approval and gateway capability rollout. No registry/marketplace listing is asserted by this source release.

## Build the installable ZIP

Maintainers need .NET 9 SDK and Python 3.10+. The bootstrap uses checksum-pinned official nopCommerce 4.90.8 references. The default SDK dependency is the published `Lunixi.Sdk` 1.0.1 package from NuGet.org, whose registry package hash is verified in the committed lock:

```bash
python3 scripts/bootstrap.py
python3 scripts/build.py --restore
```

An already downloaded trusted 4.90.8 NoSource directory can be supplied with `--nop-runtime`. Default package restores use an isolated `.cache/registry-mode-packages` cache and NuGet.org only; an old `.cache/nuget` directory is not used implicitly. For explicit development acceptance, `--sdk-source` selects a reviewed 1.0.1 SDK checkout and `--sdk-package` selects a reviewed local `Lunixi.Sdk.1.0.1.nupkg`. Local-package builds use their own cache and ignored `packages.sdk-local.lock.json` files, so their bytes cannot replace registry locks. The merchant ZIP bundles runtime dependencies and needs no restore on the merchant server.

Output: `artifacts/Lunixi.Payments.nopCommerce.4.90-1.0.2.zip` and its SHA-256 file. The archive has one root directory, `Payments.Lunixi`, matching the native upload contract. Platform assemblies are compile/test references and are not redistributed in the plugin ZIP.

## Install and configure

1. Back up your store and install the ZIP from **Configuration → Local plugins → Upload plugin or theme**. Restart the application, then install **Lunixi Payments**.
2. Generate a random 32-byte server encryption key and set its base64 representation as `LUNIXI_NOP_ENCRYPTION_KEY` on every application instance. Keep a secure backup outside the database and source repository. Do not replace it without migrating historical profiles.
3. Configure the plugin: enable it, enter the merchant ID, merchant SECRET key identifier, Ed25519 PKCS#8 PEM private key and webhook secret. TEST/LIVE is determined by the API key. Secret fields remain blank on subsequent views; leave them blank to preserve saved values.
4. Keep embedded mode (default), or explicitly choose optional hosted mode and, if required, authorization followed by later capture. Existing attempts retain their original request body and display mode.
5. Register the displayed **HTTPS webhook URL** in the Lunixi panel with the matching environment and payment-event subscriptions. Keep prior endpoint/credential profiles available while historical payments can need refunds or reconciliation.
6. Activate Lunixi under **Configuration → Payment methods**. Use TEST credentials on a staging store and verify your complete buyer/order workflow before activating LIVE credentials.

The store must serve checkout over HTTPS with correctly configured trusted proxy headers. For embedded checkout, allow the fixed official SDK `https://cdn.lunixi.com/lunixi-checkout.min.js` in your CSP and the Lunixi form's required network/frame origins. The plugin never renders gateway-returned HTML. Payment is denominated in the native store primary currency; display-currency conversion does not alter settlement money.

## Payment and financial safety

The native payment method creates a pending order, persists one attempt/key before network I/O, and starts the real gateway intent. It binds the actual `paymentId` and browser token, rejects conflicting aliases, and encrypts buyer payload/token storage. Customer ownership and store identity are checked before token exposure. Expired/uncertain attempts never silently get a new key; a new attempt requires an authenticated terminal failure with no captured funds.

Webhook processing verifies exact raw bytes, v2 signature/timestamp/event binding and durable replay identity. It locates only a known merchant payment, then performs authenticated status reads matching merchant, order, currency, base amount and complete settlement balances. Fee increases require the persisted order fingerprint echoed by authenticated payment metadata. The gross charged/refunded amount is stored separately; the basket total is not inflated a second time.

Captured legs never fulfill an order. Split fulfillment requires the correct parent, complete balanced aggregate capture and no compensation/reconciliation requirement; endpoint deployment and merchant eligibility remain prerequisites. Authorized payments remain on hold. Partial/unknown/reconciliation states never become paid. Promissory-note issuance requires review because issuance is not cash collection; adapt your merchant fulfillment policy before enabling that rail.

Native capture/refund/void methods require native order-edit capability. Core admin actions retain their native CSRF gates. Stable operation IDs are persisted before gateway calls. A mutation response alone cannot report success: complete authenticated settlement evidence must confirm it. Partial refunds use exact primary-currency minor units; the final basket refund includes any remaining authenticated installment fee.

nopCommerce writes native financial state after `IPaymentMethod` returns. The plugin explicitly reserves that handoff, blocks webhook races, acknowledges native order events and observes response completion. Unknown outcomes reconcile by reading the gateway rather than creating a second operation. A process crash during the small native handoff window can require operator review; the plugin retains the operation and blocks blind retries. Review the native order and verified gateway settlement before resolving that condition.

The scheduled task polls unresolved and settled attempts without starving newer orders. Native order and inbox writes use one native database transaction with per-order row locking. Ambient transaction enlistment is mandatory; MySQL financial/order/note/email tables must be InnoDB and are checked. Native built-in notifications are queued transactionally; third-party event consumers must use a durable outbox for external side effects.

## Lifecycle and support

Uninstall disables the plugin task and preserves financial attempts, operations, inbox, settings and encrypted credential profiles. Keep the server key and necessary merchant credentials through refund/dispute retention. Recurring payments are not implemented in this release. Generic SDK product access does not imply native integration for every separate Lunixi product.

English and Turkish resources use native localization. The plugin ships the original Lunixi logos. Report vulnerabilities privately through [SECURITY.md](SECURITY.md); never send real credentials or buyer data in issues.

## Verification

See [docs/VERIFICATION.md](docs/VERIFICATION.md) for executed commands and limits. Tests use official pinned native assemblies and real database transactions, not a replacement platform API. The suite covers native paid/authorized/refund/void behavior, rollback, concurrency/replay, shared SDK verification, merchant/fee/split evidence, operation-key persistence, permissions, secret protection and unknown outcomes. No live charge is made.

[Changelog](CHANGELOG.md) · [Contributing](CONTRIBUTING.md) · [MIT](LICENSE)

`FAILED` with zero capture alone does not prove definitive failure: public status does not expose outcome certainty. It remains review and cannot rotate an attempt key. Only verified `VOIDED`/`EXPIRED` with zero settlement may permit a new attempt.

The canonical browser callback is POST. A separate anonymous, bounded adapter ignores every submitted field and performs only a fixed same-origin 303 redirect. The resulting HTTPS GET restores native customer/store ownership checks before displaying any order link. Neither stage performs gateway reconciliation or treats callback fields as payment proof.
