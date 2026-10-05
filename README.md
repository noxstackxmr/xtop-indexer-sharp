# XTOP Indexer

Indexer for the XMR Token Overlay Protocol on Monero.

## Native library

`xtop_monero.dll` (Windows x64) / `libxtop_monero.so` (Linux x64) provides cryptographic functions used by [MoneroProofCrypto](Monero/MoneroProofCrypto.cs).

- [vendor/monero](Monero/Native/vendor/monero): arithmetic and Keccak sources from Monero v0.18.5.1, commit `4f92268d7c16741cfb41e5bbe2aa46cc260a9ea5`. File origins and SHA-256 hashes: [SOURCES.json](Monero/Native/vendor/monero/SOURCES.json). [License](Monero/Native/vendor/monero/LICENSE)
- [xtop_monero.c](Monero/Native/xtop_monero.c): output keys and key images
- [xtop_proofs.c](Monero/Native/xtop_proofs.c): XTOP proof functions, signatures and amount commitments
- [compat](Monero/Native/compat): stringization macro required by the Monero headers

## Build

Requires .NET 10. Run from the repository root.

Windows: [setup.ps1](Monero/Native/setup.ps1) downloads Zig 0.13.0 and verifies its checksum. Run setup once, then [build.ps1](Monero/Native/build.ps1).

```powershell
./Monero/Native/setup.ps1
./Monero/Native/build.ps1
dotnet build
```

Linux: install a C compiler and development headers, then run [build-linux.sh](Monero/Native/build-linux.sh).

```sh
sh Monero/Native/build-linux.sh
dotnet build
```

Native binaries are built in `Monero/Native/build`. Build and publish copy the matching binary beside the application and the Monero license to `licenses/monero/LICENSE`. Rebuild the native library after changing its sources. Binaries and compiler caches are excluded from Git.

## API

| GET | Response |
|---|---|
| `/api/collections?page=1&pageSize=20` | Collection summaries, newest first |
| `/api/collections/{id}` | Terms, current metadata locations, current control, latest change and creation data |
| `/api/items?page=1&pageSize=20` | Items; optional `collectionId` and `status` filters |
| `/api/items/{itemId}` | Owner, output, metadata, primary purchase and burn data |
| `/api/listings?page=1&pageSize=20` | Active listings, prices, payout keys and output maturity; optional `collectionId` filter |
| `/api/listings/{listingId}` | Listing terms, output and current status, including purchased, cancelled or burned listings |
| `/api/items/{itemId}/history?page=1&pageSize=20` | Item preparation, purchases, listings, cancellations and external spends, newest first |
