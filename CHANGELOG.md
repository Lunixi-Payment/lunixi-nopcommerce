# Changelog

## 1.1.0 — 2026-11-16

- A captured or authorized payment for a cancelled order is no longer a 503 retry loop: the plugin adds an admin-only review note naming the payment, sets the `Lunixi.PaymentReview` order attribute and acknowledges the webhook. The order is never marked paid automatically.
- A new payment linked to a known order (a late bank transfer accepted after the original payment expired) is verified with an authenticated read against merchant, order, currency and amount. With funds it gets the same idempotent review note and attribute with the new payment ID; without funds the event is acknowledged with no change. Payments of unknown orders are still rejected.
- Build on `Lunixi.Sdk` 1.3.0 from NuGet.org (was 1.0.1); no plugin API change.
- Document the 1–3 business day bank transfer waiting period and the review flag.

## 1.0.2 — 2026-11-01

- Include the canonical money-string migration guide in the standalone merchant ZIP and require it at both artifact and publication gates.
- Require the tested source commit to match the release tag before publishing the immutable ZIP.

## 1.0.1 — 2026-11-01

- Align the money-string regression test with the enforced formatter; retain the exact monetary validation and actual NuGet 1.0.1 dependency.

## 1.0.0 — 2026-11-01

- Breaking: use the 1.0.0 SDK canonical minor-string API; reject numeric monetary evidence.
- Exact decimal text and currency conversion; see [migration](MIGRATION-1.0.md).

## 0.1.2 — 2026-10-31

- Default new checkout configuration to the existing Lunixi form on the merchant site; retain the optional hosted selector and explicit merchant settings.
- Redirect only to a validated backend `paymentPageUrl`; its opaque token can differ from the embedded form token. Missing URLs keep embedded checkout available and hosted checkout on hold without creating another intent/key.
- Preserve historical checkout modes, encrypted credentials/tokens, immutable request bodies and existing idempotency/3DS/split return behavior.
- Store the backend URL with the browser token in authenticated-encrypted versioned state; read legacy token-only state without rewriting financial history.

## 0.1.1 — 2026-10-31

- Initialize the optional local NuGet feed in all bootstrap modes so a clean GitHub export restores without NU1301.
- Omit an absent optional local source from direct MSBuild restore and verify an isolated clean-export locked restore.
- Add a bounded read-only POST callback adapter with a fixed same-origin 303 and owner/store-checked native GET.
- Retain shared Lunixi.Sdk 0.1.0 and authenticated financial behavior.

## 0.1.0 — 2026-10-31

- Native nopCommerce 4.90 payment method, compiled against official 4.90.8 assemblies.
- Hosted pay.lunixi.com and embedded shared checkout form; methods remain managed by the Lunixi panel.
- Encrypted immutable credential profiles, persistent checkout attempts, per-order operation reservations and atomic webhook/native-order application.
- Authenticated merchant/order/currency/amount settlement evidence, fee fingerprint and complete split-parent checks.
- Native capture, full/partial refund and void integration, stable keys, uncertain-outcome reconciliation and native post-return write coordination.
- English/Turkish native resources, nondestructive uninstall, bundled private SDK context and meaningful native database/security tests.

Source publication does not imply NuGet.org publication, nopCommerce Marketplace approval or gateway feature rollout.
