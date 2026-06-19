from __future__ import annotations

import os
import base64
import hmac as _hmac
from typing import Optional

from cryptography.hazmat.primitives.asymmetric import rsa, padding
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from cryptography.hazmat.primitives.hmac import HMAC
from cryptography.hazmat.primitives.kdf.scrypt import Scrypt
from cryptography.exceptions import InvalidKey

RSA_KEY_SIZE = 2048 #.....0x1
AES_KEY_SIZE = 32 #.......0x2
GCM_NONCE_SIZE = 12 #.....0x3
SALT_SIZE = 32 #..........0x4
SCRYPT_N = 2 ** 14 #......0x5
SCRYPT_R = 8 #............0x6
SCRYPT_P = 1 #............0x7

_OAEP = padding.OAEP(
    mgf=padding.MGF1(algorithm=hashes.SHA256()),
    algorithm=hashes.SHA256(),
    label=None,
)

EncryptedPackage = dict[str, bytes]


def generate_key_pair(key_size: int = RSA_KEY_SIZE):
    private_key = rsa.generate_private_key(public_exponent=65537, key_size=key_size)
    return private_key, private_key.public_key()


def serialize_private_key(private_key, password: Optional[bytes] = None) -> bytes:
    encryption = (
        serialization.BestAvailableEncryption(password)
        if password
        else serialization.NoEncryption()
    )
    return private_key.private_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PrivateFormat.PKCS8,
        encryption_algorithm=encryption,
    )


def serialize_public_key(public_key) -> bytes:
    return public_key.public_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PublicFormat.SubjectPublicKeyInfo,
    )


def load_private_key(pem: bytes, password: Optional[bytes] = None):
    return serialization.load_pem_private_key(pem, password=password)


def load_public_key(pem: bytes):
    return serialization.load_pem_public_key(pem)


def from_b64(data: str) -> bytes:
    return base64.b64decode(data)


def to_b64(data: bytes) -> str:
    return base64.b64encode(data).decode()


def load_public_key_b64(b64: str):
    return load_public_key(from_b64(b64))


def load_private_key_b64(b64: str, password: Optional[bytes] = None):
    return load_private_key(from_b64(b64), password=password)


def encrypt(plaintext: bytes, public_key) -> EncryptedPackage:
    aes_key    = os.urandom(AES_KEY_SIZE)
    nonce      = os.urandom(GCM_NONCE_SIZE)
    ciphertext = AESGCM(aes_key).encrypt(nonce, plaintext, associated_data=None)
    return {
        "encrypted_aes_key": public_key.encrypt(aes_key, _OAEP),
        "nonce":              nonce,
        "ciphertext":         ciphertext,
    }


def decrypt(package: EncryptedPackage, private_key) -> bytes:
    aes_key = private_key.decrypt(package["encrypted_aes_key"], _OAEP)
    return AESGCM(aes_key).decrypt(package["nonce"], package["ciphertext"], associated_data=None)


def sha256(data: bytes) -> bytes:
    h = hashes.Hash(hashes.SHA256())
    h.update(data)
    return h.finalize()


def sha512(data: bytes) -> bytes:
    h = hashes.Hash(hashes.SHA512())
    h.update(data)
    return h.finalize()


def hmac_sign(data: bytes, key: bytes) -> bytes:
    h = HMAC(key, hashes.SHA256())
    h.update(data)
    return h.finalize()


def hmac_verify(data: bytes, key: bytes, signature: bytes) -> bool:
    return _hmac.compare_digest(hmac_sign(data, key), signature)


def derive_key(
    password: bytes,
    salt: Optional[bytes] = None,
    length: int = AES_KEY_SIZE,
) -> tuple[bytes, bytes]:
    if salt is None:
        salt = os.urandom(SALT_SIZE)
    key = Scrypt(salt=salt, length=length, n=SCRYPT_N, r=SCRYPT_R, p=SCRYPT_P).derive(password)
    return key, salt


def hash_password(password: bytes) -> bytes:
    salt   = os.urandom(SALT_SIZE)
    digest = Scrypt(salt=salt, length=32, n=SCRYPT_N, r=SCRYPT_R, p=SCRYPT_P).derive(password)
    return salt + digest


def verify_password(password: bytes, stored: bytes) -> bool:
    salt, digest = stored[:SALT_SIZE], stored[SALT_SIZE:]
    try:
        Scrypt(salt=salt, length=32, n=SCRYPT_N, r=SCRYPT_R, p=SCRYPT_P).verify(password, digest)
        return True
    except InvalidKey:
        return False