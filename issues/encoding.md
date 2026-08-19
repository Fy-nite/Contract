# New Module: Encoding - Character Encoding

## [New Module] `Encoding` - encoding and decoding utilities

Character encoding helpers beyond what `Convert` provides.

Binary protocol work, network communication, file format parsing.

### Proposed Signatures

```
Encoding.UTF8Encode(string str) -> int[]
Encoding.UTF8Decode(int[] bytes) -> string
Encoding.ASCIIEncode(string str) -> int[]
Encoding.ASCIIDecode(int[] bytes) -> string
Encoding.UnicodeEncode(string str) -> int[]
Encoding.UnicodeDecode(int[] bytes) -> string
Encoding.GetByteCount(string str) -> int
Encoding.GetByteCountUTF8(string str) -> int
```

### Notes

- Could also provide `HexEncode` / `HexDecode` as alternatives to `Convert.ToByteArrayHex`
- Consider URL encoding: `Encoding.URLEncode(string) -> string` and `Encoding.URLDecode(string) -> string`
- Consider HTML encoding: `Encoding.HTMLEncode(string) -> string` and `Encoding.HTMLDecode(string) -> string`
