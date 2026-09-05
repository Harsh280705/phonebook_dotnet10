from pydantic import (
    BaseModel,
    EmailStr,
    field_validator
)

from typing import Optional
from datetime import datetime
import re


class ContactBase(BaseModel):

    name: Optional[str] = None

    phone_number: Optional[str] = None

    email: Optional[EmailStr] = None

    address: Optional[str] = None


    @field_validator("name")
    @classmethod
    def validate_name(cls, value):

        if value is None:
            return value


        value = value.strip()


        if not re.match(
            r"^[A-Za-zÀ-ÿ][A-Za-zÀ-ÿ\s'-]{1,99}$",
            value
        ):

            raise ValueError(
                "Name must contain only letters, spaces, apostrophes or hyphens."
            )


        return value


    @field_validator("phone_number")
    @classmethod
    def validate_phone_number(cls, value):

        if value is None:
            return value


        value = value.strip()


        if not re.fullmatch(r"\+?[0-9()\s-]+", value):

            raise ValueError(
                "Phone number may contain digits, a leading +, spaces, hyphens, or parentheses."
            )

        digit_count = len(re.sub(r"\D", "", value))
        if digit_count < 8 or digit_count > 15:
            raise ValueError(
                "Phone number must contain between 8 and 15 digits."
            )


        return value


    @field_validator("address")
    @classmethod
    def validate_address(cls, value):

        if value is None:
            return value


        value = value.strip()


        if not value:
            return None


        if len(value) < 5 or len(value) > 255:

            raise ValueError(
                "Address must be between 5 and 255 characters."
            )


        if not re.search(
            r"[A-Za-zÀ-ÿ]",
            value
        ):

            raise ValueError(
                "Address must contain at least one letter."
            )


        if not re.match(
            r"^[A-Za-zÀ-ÿ0-9\s,.'#/-]+$",
            value
        ):

            raise ValueError(
                "Address contains invalid characters."
            )


        return value


class ContactCreate(ContactBase):

    name: str

    phone_number: str


class ContactUpdate(ContactBase):
    pass


class ContactResponse(BaseModel):

    id: int

    name: str

    phone_number: str

    email: Optional[EmailStr] = None

    address: Optional[str] = None

    created_at: datetime


    class Config:
        from_attributes = True


class ContactPage(BaseModel):

    items: list[ContactResponse]

    page: int

    limit: int

    total: int

    total_pages: int