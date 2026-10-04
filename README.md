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
| `/api/collections/{id}` | Terms, metadata locations, creation transaction and creation outputs |

`id` is the 64-character hex CREATE transaction ID. `page` starts at 1; `pageSize` is 1–100. Invalid parameters return 400; a missing collection returns 404. At most eight collection requests run concurrently; excess requests return 429.

Amounts use decimal strings in Monero atomic units (1 XMR = 10^12). Keys and hashes use lowercase hex. Times use UTC. `scannedTip` identifies the scanned chain snapshot, not completion of all message processing. Responses are read from a consistent database snapshot and are not cached.

Metadata URLs are returned as stored; external JSON and images are not fetched. `creationOutputs` describes the CREATE outputs, not their current spend status. These methods currently expose creation data; SALE and REVEAL processing will be added separately.

## License

[AGPL-3.0-only](LICENSE). Vendored Monero code retains its [upstream license](Monero/Native/vendor/monero/LICENSE).
