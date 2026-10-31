# Verification record — 0.1.1

Verified on macOS arm64 with .NET SDK 9.0.320/runtime 9.0.14, official nopCommerce 4.90.8 and a dedicated MariaDB 11/InnoDB TEST database. No LIVE gateway charge was made.

## Pinned native platform

- Official source release: `nopSolutions/nopCommerce`, `release-4.90.8`, source commit `918444773367deb734b93d3ad80e17325d6c147a`.
- Source archive SHA-256: `32ec3b81d90651fcbfb5767da8e1ac8bf21d50e8bf5f3d129848b17ece0f3d00`.
- Official Linux x64 NoSource archive SHA-256: `054f6c6796a627c74b06f89bc2504a32755a7c0e6fe33503132cf7e85419a1c7`.
- Native references are not shipped to merchants. Platform API declarations are not replaced by test stubs.
- Shared SDK: Lunixi.Sdk 0.1.0, built from the separately reviewed `lunixi-dotnet` source. An explicit local nupkg input is also supported without assuming NuGet.org availability.

## Reproduce

Use a disposable database called exactly `lunixi_nop`; the native tests create tables and exercise transactions. Its account must own only that TEST schema. Supply a private JSON file containing `host`, integer `port`, `database`, `username` and `password`. Keep it outside public source. The test fixture refuses another database name.

```bash
python3 scripts/bootstrap.py --nop-runtime /absolute/path/to/nop-4.90.8 --sdk-source /absolute/path/to/lunixi-dotnet
export NOP_RUNTIME_PATH=/absolute/path/to/nop-4.90.8
export LUNIXI_SDK_PROJECT=/absolute/path/to/lunixi-dotnet/src/Lunixi.Sdk/Lunixi.Sdk.csproj
export LUNIXI_TEST_DB_CONFIG=/private/path/to/test-db.json
export LUNIXI_PLUGIN_STAGING="$PWD/artifacts/staging"
dotnet restore tests/Lunixi.NopCommerce.Tests/Lunixi.NopCommerce.Tests.csproj --locked-mode
python3 scripts/build.py
dotnet test tests/Lunixi.NopCommerce.Tests/Lunixi.NopCommerce.Tests.csproj -c Release --no-restore
for project in src/Nop.Plugin.Payments.Lunixi/Nop.Plugin.Payments.Lunixi.csproj src/Lunixi.NopCommerce.Gateway/Lunixi.NopCommerce.Gateway.csproj tests/Lunixi.NopCommerce.Tests/Lunixi.NopCommerce.Tests.csproj; do
  dotnet format "$project" --verify-no-changes --no-restore
done
node --check src/Nop.Plugin.Payments.Lunixi/Assets/checkout.js
python3 -m py_compile scripts/bootstrap.py scripts/build.py
dotnet list tests/Lunixi.NopCommerce.Tests/Lunixi.NopCommerce.Tests.csproj package --vulnerable --include-transitive --format json
gitleaks dir . --config .gitleaks.toml --redact --no-banner
# Run from outside the source tree so its build-output exclusions do not apply.
(cd /tmp && gitleaks dir /absolute/path/to/nopcommerce/artifacts/staging --redact --no-banner)
```

The explicit-nupkg build uses an isolated `.cache/package-mode-packages` NuGet cache, records the supplied artifact hash and evaluates its package lock. Source SDK builds and CI use their separate committed source-mode dependency locks. Bootstrap checks the official platform download SHA; trusted explicit local inputs are selected by the maintainer.

## Executed results

- **44 tests passed, 0 failed, 0 skipped** on the pinned native runtime and real MariaDB fixture.
- Release artifact build: **0 warnings, 0 errors**, in both shared SDK source mode and explicit local-nupkg mode.
- Source-mode locked restore passed; C# formatting, JavaScript syntax and Python compilation checks passed.
- NuGet transitive vulnerability audit returned no vulnerable packages.
- Gitleaks default-rule scans of public source and the installable staging tree returned no findings. The artifact scan used a separate default-rules-only config, without the source build-output exclusions.
- EN/TR documentation passed the docs site's API-path and amount-field checks.

## Coverage and limits

The native suite exercises official `MySqlNopDataProvider`, actual mappings and repositories, real `OrderService`, `OrderProcessingService`, and the plugin's actual migrations against MariaDB. Network I/O is deterministic TEST transport. Ancillary email/customer/shipping collaborators are controlled test doubles; this is native financial-service/database acceptance, not a claim that a complete store was installed and clicked through in a browser.

Checks include native paid/authorized/capture/partial-and-full-refund/void state, row-lock concurrency, durable replay conflicts, rollback of native order/notes/inbox, transaction enlistment, rejection of nontransactional order storage, ownership/capability attributes, encrypted secret purposes, immutable currency/display snapshots, persisted operation identity/retention, unknown mutation recovery, merchant/base/fee/split settlement evidence and real packaged private SDK/native-crypto loading. The scheduler reserves rotating capacity for unresolved and settled orders.

SQL Server/PostgreSQL dialects are implemented but were not exercised against local native databases. A third-party event consumer performing external effects before transaction commit requires its own durable outbox. Full browser installation, end-to-end merchant onboarding, live gateway/3-D Secure and real split/insurance product flows remain merchant staging acceptance requirements. The private SDK was loaded and its Ed25519/native assets exercised on macOS arm64; other bundled OS/RID assets follow the upstream NSec/libsodium distribution and require their own deployment smoke check.

GitHub CI is defined with pinned actions/native image and the same native MariaDB tests. A checked-in workflow is not evidence that remote CI has run. No marketplace approval or registry publication is asserted.

The FAILED-certainty regression verifies that an authenticated FAILED status with zero capture remains review and retains its existing attempt/key. VOIDED/EXPIRED zero-settlement observations remain separately covered. No undocumented outcome-certainty field is assumed.

## 0.1.1 clean-export restore correction

The source-mode bootstrap now creates the optional `.cache/nuget` directory before restore. Direct MSBuild restore only includes that feed when it exists. This fixes public CI NU1301 on a clean GitHub checkout without changing financial behavior or the shared SDK 0.1.0 pin.

`python3 scripts/verify-clean-restore.py --nop-runtime /trusted/native/path --sdk-source /trusted/sdk/path` copies both projects without caches/build outputs and performs a real locked restore with an initially empty NuGet package cache. CI runs this regression before native tests.

Browser-return regressions cover the actual native MVC GET/POST action discovery, the narrow CSRF exception, anonymous bounded POST without dependency/order/session/gateway access, fixed same-origin 303 despite hostile fields, oversize/prefix rejection and owner-checked native GET. No callback field changes financial state.
