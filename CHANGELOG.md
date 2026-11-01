# Changelog

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
