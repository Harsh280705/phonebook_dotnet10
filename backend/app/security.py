import base64
import hashlib
import hmac
import secrets


def hash_password(password: str) -> str:
    salt = secrets.token_bytes(16)
    digest = hashlib.scrypt(
        password.encode("utf-8"), salt=salt, n=2**14, r=8, p=1
    )
    return (
        "scrypt$"
        + base64.urlsafe_b64encode(salt).decode()
        + "$"
        + base64.urlsafe_b64encode(digest).decode()
    )


def verify_password(password: str, stored_hash: str) -> bool:
    try:
        algorithm, encoded_salt, encoded_digest = stored_hash.split("$", 2)
        if algorithm != "scrypt":
            return False
        salt = base64.urlsafe_b64decode(encoded_salt.encode())
        expected_digest = base64.urlsafe_b64decode(encoded_digest.encode())
        actual_digest = hashlib.scrypt(
            password.encode("utf-8"), salt=salt, n=2**14, r=8, p=1
        )
        return hmac.compare_digest(actual_digest, expected_digest)
    except (ValueError, TypeError):
        return False


def create_session_token() -> str:
    return secrets.token_urlsafe(32)


def hash_session_token(token: str) -> str:
    return hashlib.sha256(token.encode("utf-8")).hexdigest()