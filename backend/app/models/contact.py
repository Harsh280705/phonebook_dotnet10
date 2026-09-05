from sqlalchemy import Column, DateTime, ForeignKey, Integer, String, Text
from sqlalchemy.sql import func

from app.database.connection import Base


class Contact(Base):
    __tablename__ = "contacts"

    id = Column(Integer, primary_key=True, index=True)

    user_id = Column(
        Integer,
        ForeignKey("users.id"),
        nullable=True,
        index=True
    )

    name = Column(
        String(255),
        nullable=False
    )

    phone_number = Column(
        String(50),
        unique=True,
        nullable=False,
        index=True
    )

    email = Column(
        String(255),
        unique=True,
        nullable=True
    )

    address = Column(
        Text,
        nullable=True
    )

    created_at = Column(
        DateTime(timezone=True),
        server_default=func.now()
    )