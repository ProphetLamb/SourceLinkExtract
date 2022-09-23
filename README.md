# SourceLinkExtract

[NuGet here](https://www.nuget.org/packages/SourceLinkExtract/)

A tool to extract source files using sourcelink metadata.

```bash
extract ./test.dll ./test-meta/
```

If you for some reason have to use Windows, convert your compilation to portable pdb https://github.com/dotnet/symreader-converter

## Argument 1
The input filepath can be a pdb, or compilation (exe/dll/...).

```bash
./test.dll
./test.pdb
./test.exe
```

## Argument 2
Filepath of the metadata dump, in the following format:

```json
{
  "link": {
    "documents": {
      "/_/*": "https://raw.githubusercontent.com/ProphetLamb/Surreal.Net/9050c906117c795ca385fd52b75062771a2a8816/*"
    }
  },
  "docs": [
    {
      "name": "src/Abstractions/Database.cs",
      "lang": "3f5162f8-07c6-11d3-9053-00c04fa302a1",
      "algo": "8829d00f-11b8-4213-878b-770e8597ac16",
      "hash": "sksvYzgtjDMO34efmUFUzcmtYiT/TuXUrURrbf9dNwk="
    },
    [...]
  ]
}

```

## Argument 3
Output directorypath of extracted sourcecode.

```
./meta/src/Abstractions/Database.cs
[...]
```
