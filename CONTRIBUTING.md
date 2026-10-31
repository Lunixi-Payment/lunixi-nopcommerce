# Contributing

The public repository is published from Lunixi's integration source. Maintainers apply accepted changes in that source and credit contributors. Report ordinary defects with plugin/platform versions and a sanitized request reference. Report vulnerabilities privately using [SECURITY.md](SECURITY.md).

Use .NET 9, Python 3.10+ and the checksum-pinned official nopCommerce references. The shared SDK comes from the separate `lunixi-dotnet` source or an explicit trusted local package; do not copy its signing or transport code into the plugin.

Run the documented native database, shared-SDK bridge, formatting, dependency-audit and ZIP checks. Financial changes require meaningful cases covering unknown outcomes, duplicate/concurrent delivery, complete authenticated balances and rollback of both native order and inbox. A platform stub or a gateway HTTP 200 is not proof of payment.

Keep all customer/admin text in native English and Turkish resources. Preserve the original Lunixi brand assets, native permissions/CSRF protection and financial history on uninstall. Do not add duplicate payment-method switches; the Lunixi form settings are authoritative.
