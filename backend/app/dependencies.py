from datetime import datetime, timezone

from fastapi import Depends, HTTPException, Request, status
from sqlalchemy.orm import Session

from app.database.connection import get_db
from app.models.user import AuthSession, User
from app.security import hash_session_token


SESSION_COOKIE = "phonebook_session"


def get_current_user(
    request: Request,
    db: Session = Depends(get_db)
) -> User:
    token = request.cookies.get(SESSION_COOKIE)
    if not token:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Authentication required."
        )

    session = (
        db.query(AuthSession)
        .filter(
            AuthSession.token_hash == hash_session_token(token),
            AuthSession.expires_at > datetime.now(timezone.utc)
        )
        .first()
    )

    if not session:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Session expired or invalid."
        )

    user = db.query(User).filter(User.id == session.user_id).first()
    if not user:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="User not found."
        )

    return user