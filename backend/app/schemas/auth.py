from datetime import datetime

from pydantic import BaseModel, EmailStr, field_validator


class RegisterRequest(BaseModel):
    username: str
    email: EmailStr
    password: str

    @field_validator("username")
    @classmethod
    def validate_username(cls, value):
        value = value.strip()
        if len(value) < 3 or len(value) > 100:
            raise ValueError("Username must be between 3 and 100 characters.")
        return value

    @field_validator("password")
    @classmethod
    def validate_password(cls, value):
        if len(value) < 8:
            raise ValueError("Password must contain at least 8 characters.")
        return value


class LoginRequest(BaseModel):
    identifier: str
    password: str


class UserResponse(BaseModel):
    id: int
    username: str
    email: EmailStr
    created_at: datetime

    class Config:
        from_attributes = True