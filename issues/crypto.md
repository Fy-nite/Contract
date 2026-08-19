# New Module: Crypto - Hashing and Encoding

## [New Module] `Crypto` - cryptographic hashing and HMAC

Cryptographic hash functions and HMAC.

Checksums, password hashing, data integrity verification, API authentication.

### Proposed Signatures

```
Crypto.MD5(string input) -> string
Crypto.SHA1(string input) -> string
Crypto.SHA256(string input) -> string
Crypto.SHA512(string input) -> string
Crypto.HMACSHA256(string key, string message) -> string
Crypto.MD5File(string path) -> string
Crypto.SHA256File(string path) -> string
Crypto.MD5Bytes(int[] data) -> string
Crypto.SHA256Bytes(int[] data) -> string
```

### Examples

```
Crypto.SHA256("hello world")                              // hex hash string
Crypto.HMACSHA256("secret-key", "message-body")            // HMAC for API auth
Crypto.MD5File("document.pdf")                             // file integrity check
Crypto.SHA256Bytes(File.ReadAllBytes("image.png"))          // hash binary data
```

### Notes

- All return lowercase hex strings
- File variants read the entire file - consider streaming for large files in the future
- Could add `Verify` functions: `Crypto.VerifySHA256(data, expectedHash) -> bool`
