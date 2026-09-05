from datetime import datetime, timedelta, timezone

from fastapi import APIRouter, Depends, HTTPException, Request, Response, status
from sqlalchemy import or_
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session

from app.database.connection import get_db
from app.dependencies import SESSION_COOKIE, get_current_user
from app.models.contact import Contact
from app.models.user import AuthSession, User
from app.schemas.auth import LoginRequest, RegisterRequest, UserResponse
from app.security import (
    create_session_token,
    hash_password,
    hash_session_token,
    verify_password,
)


router = APIRouter(prefix="/auth", tags=["Authentication"])
SESSION_DURATION = timedelta(days=7)


def set_session_cookie(response: Response, token: str):
    response.set_cookie(
        key=SESSION_COOKIE,
        value=token,
        max_age=int(SESSION_DURATION.total_seconds()),
        httponly=True,
        samesite="lax",
        secure=False
    )


@router.post(
    "/register",
    response_model=UserResponse,
    status_code=status.HTTP_201_CREATED
)
def register(
    user_data: RegisterRequest,
    response: Response,
    db: Session = Depends(get_db)
):
    if db.query(User).filter(User.username == user_data.username).first():
        raise HTTPException(status_code=409, detail="Username already exists.")
    if db.query(User).filter(User.email == user_data.email).first():
        raise HTTPException(status_code=409, detail="Email already exists.")

    user = User(
        username=user_data.username,
        email=user_data.email,
        password_hash=hash_password(user_data.password)
    )

    try:
        db.add(user)
        db.flush()

        if db.query(User).count() == 1:
            db.query(Contact).filter(Contact.user_id.is_(None)).update(
                {Contact.user_id: user.id}, synchronize_session=False
            )

        token = create_session_token()
        db.add(AuthSession(
            token_hash=hash_session_token(token),
            user_id=user.id,
            expires_at=datetime.now(timezone.utc) + SESSION_DURATION
        ))
        db.commit()
        db.refresh(user)
    except IntegrityError:
        db.rollback()
        raise HTTPException(status_code=409, detail="Username or email already exists.")

    set_session_cookie(response, token)
    return user


@router.post("/login", response_model=UserResponse)
def login(
    credentials: LoginRequest,
    response: Response,
    db: Session = Depends(get_db)
):
    user = (
        db.query(User)
        .filter(or_(
            User.username == credentials.identifier,
            User.email == credentials.identifier
        ))
        .first()
    )

    if not user or not verify_password(credentials.password, user.password_hash):
        raise HTTPException(status_code=401, detail="Invalid username/email or password.")

    token = create_session_token()
    db.add(AuthSession(
        token_hash=hash_session_token(token),
        user_id=user.id,
        expires_at=datetime.now(timezone.utc) + SESSION_DURATION
    ))
    db.commit()
    set_session_cookie(response, token)
    return user


@router.get("/me", response_model=UserResponse)
def current_user(user: User = Depends(get_current_user)):
    return user


@router.post("/logout")
def logout(
    request: Request,
    response: Response,
    db: Session = Depends(get_db)
):
    token = request.cookies.get(SESSION_COOKIE)
    if token:
        db.query(AuthSession).filter(
            AuthSession.token_hash == hash_session_token(token)
        ).delete(synchronize_session=False)
        db.commit()
    response.delete_cookie(SESSION_COOKIE)
    return {"message": "Logged out successfully."}