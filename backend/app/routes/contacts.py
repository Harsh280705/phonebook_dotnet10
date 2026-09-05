import csv
import io
import re

from fastapi import APIRouter, Depends, File, HTTPException, Query, Response, UploadFile, status
from pydantic import ValidationError
from sqlalchemy.orm import Session
from sqlalchemy.exc import IntegrityError
from sqlalchemy import or_

from app.database.connection import get_db
from app.dependencies import get_current_user
from app.models.contact import Contact
from app.models.user import User
from app.schemas.contact import (
    ContactCreate,
    ContactPage,
    ContactResponse,
    ContactUpdate
)


router = APIRouter(
    prefix="/contacts",
    tags=["Contacts"]
)


# CREATE CONTACT
@router.post(
    "/",
    response_model=ContactResponse,
    status_code=status.HTTP_201_CREATED
)
def create_contact(
    contact: ContactCreate,
    db: Session = Depends(get_db),
    user: User = Depends(get_current_user)
):

    # Check duplicate phone number
    existing_phone = (
        db.query(Contact)
        .filter(Contact.phone_number == contact.phone_number)
        .first()
    )

    if existing_phone:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="This phone number already exists."
        )

    # Check duplicate email
    if contact.email:
        existing_email = (
            db.query(Contact)
            .filter(Contact.email == contact.email)
            .first()
        )

        if existing_email:
            raise HTTPException(
                status_code=status.HTTP_409_CONFLICT,
                detail="This email address already exists."
            )

    new_contact = Contact(
        name=contact.name,
        phone_number=contact.phone_number,
        email=contact.email,
        address=contact.address,
        user_id=user.id
    )

    try:
        db.add(new_contact)
        db.commit()
        db.refresh(new_contact)

        return new_contact

    except IntegrityError:
        db.rollback()

        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="A contact with this phone number or email already exists."
        )


# GET ALL CONTACTS
@router.get(
    "/",
    response_model=ContactPage
)
def get_contacts(
    db: Session = Depends(get_db),
    user: User = Depends(get_current_user),
    page: int = Query(1, ge=1),
    limit: int = Query(10, ge=1, le=100),
    search: str | None = None
):

    query = db.query(Contact).filter(Contact.user_id == user.id)

    if search and search.strip():
        search_pattern = f"%{search.strip()}%"
        query = query.filter(
            or_(
                Contact.name.ilike(search_pattern),
                Contact.phone_number.ilike(search_pattern),
                Contact.email.ilike(search_pattern)
            )
        )

    total = query.count()
    total_pages = max((total + limit - 1) // limit, 1)

    contacts = (
        query
        .order_by(Contact.name.asc())
        .offset((page - 1) * limit)
        .limit(limit)
        .all()
    )

    return {
        "items": contacts,
        "page": page,
        "limit": limit,
        "total": total,
        "total_pages": total_pages
    }


@router.get("/export")
def export_contacts(
    db: Session = Depends(get_db),
    user: User = Depends(get_current_user)
):
    contacts = (
        db.query(Contact)
        .filter(Contact.user_id == user.id)
        .order_by(Contact.name.asc())
        .all()
    )

    output = io.StringIO()
    writer = csv.writer(output)
    writer.writerow(["Name", "Phone number", "Email", "Address"])
    for contact in contacts:
        writer.writerow([
            contact.name,
            f'="{contact.phone_number}"',
            contact.email or "",
            contact.address or ""
        ])

    return Response(
        content=output.getvalue(),
        media_type="text/csv",
        headers={
            "Content-Disposition": "attachment; filename=phonebook.csv"
        }
    )


def normalize_header(header: str) -> str:
    return re.sub(r"[^a-z0-9]+", "_", header.strip().lower()).strip("_")


def get_csv_value(row: dict, aliases: set[str]) -> str:
    for header, value in row.items():
        if normalize_header(header or "") in aliases:
            return (value or "").strip()
    return ""


def normalize_import_phone(value: str) -> str:
    phone_number = value.strip()

    excel_text_match = re.fullmatch(r'="([^"]*)"', phone_number)
    if excel_text_match:
        phone_number = excel_text_match.group(1).strip()
    elif phone_number.startswith("'"):
        phone_number = phone_number[1:].strip()

    if re.fullmatch(
        r"[+-]?(?:\d+(?:\.\d*)?|\.\d+)[eE][+-]?\d+",
        phone_number
    ):
        raise ValueError(
            "Phone number is in scientific notation and cannot be recovered safely."
        )

    return phone_number


@router.post("/import")
async def import_contacts(
    file: UploadFile = File(...),
    db: Session = Depends(get_db),
    user: User = Depends(get_current_user)
):
    if not file.filename or not file.filename.lower().endswith(".csv"):
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Please upload a CSV file."
        )

    try:
        contents = await file.read()
        if not contents:
            raise HTTPException(
                status_code=status.HTTP_400_BAD_REQUEST,
                detail="The CSV file is empty."
            )
        text_content = contents.decode("utf-8-sig")
    except UnicodeDecodeError:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="The CSV file must use UTF-8 encoding."
        )

    reader = csv.DictReader(io.StringIO(text_content))
    headers = reader.fieldnames or []
    normalized_headers = {normalize_header(header or "") for header in headers}

    name_aliases = {"name", "full_name"}
    first_name_aliases = {"first_name", "firstname"}
    last_name_aliases = {"last_name", "lastname"}
    phone_aliases = {"phone", "phone_number", "mobile", "mobile_number"}
    email_aliases = {"email", "email_address"}
    address_aliases = {"address", "street_address"}
    city_aliases = {"city", "town"}
    country_aliases = {"country", "country_name"}

    has_name = bool(normalized_headers & (name_aliases | first_name_aliases | last_name_aliases))
    has_phone = bool(normalized_headers & phone_aliases)
    if not has_name or not has_phone:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="CSV must include a name or first/last name column and a phone column."
        )

    existing_phones = {
        phone for (phone,) in db.query(Contact.phone_number).all()
    }
    existing_emails = {
        email for (email,) in db.query(Contact.email).all() if email
    }
    seen_phones = set(existing_phones)
    seen_emails = set(existing_emails)

    summary = {
        "total_rows": 0,
        "imported": 0,
        "skipped_duplicates": 0,
        "invalid_rows": 0,
        "invalid_names": 0,
        "invalid_phone_numbers": 0,
        "invalid_emails": 0,
        "invalid_addresses": 0,
        "row_errors": []
    }

    for row_number, row in enumerate(reader, start=2):
        summary["total_rows"] += 1

        name = get_csv_value(row, name_aliases)
        if not name:
            name = " ".join(filter(None, [
                get_csv_value(row, first_name_aliases),
                get_csv_value(row, last_name_aliases)
            ]))

        phone_number = get_csv_value(row, phone_aliases)
        email = get_csv_value(row, email_aliases)
        address = get_csv_value(row, address_aliases)
        if not address:
            address = " ".join(filter(None, [
                get_csv_value(row, city_aliases),
                get_csv_value(row, country_aliases)
            ]))

        try:
            phone_number = normalize_import_phone(phone_number)
            contact_data = ContactCreate.model_validate({
                "name": name,
                "phone_number": phone_number,
                "email": email or None,
                "address": address or None
            })
        except (ValidationError, ValueError) as error:
            summary["invalid_rows"] += 1
            if isinstance(error, ValidationError):
                errors = error.errors()
                fields = {str(item["loc"][0]) for item in errors}
                error_message = "; ".join(item["msg"] for item in errors)
            else:
                fields = {"phone_number"}
                error_message = str(error)
            if "name" in fields:
                summary["invalid_names"] += 1
            if "phone_number" in fields:
                summary["invalid_phone_numbers"] += 1
            if "email" in fields:
                summary["invalid_emails"] += 1
            if "address" in fields:
                summary["invalid_addresses"] += 1
            summary["row_errors"].append({
                "row": row_number,
                "error": error_message
            })
            continue

        if (
            contact_data.phone_number in seen_phones
            or (
                contact_data.email
                and contact_data.email in seen_emails
            )
        ):
            summary["skipped_duplicates"] += 1
            continue

        new_contact = Contact(
            name=contact_data.name,
            phone_number=contact_data.phone_number,
            email=contact_data.email,
            address=contact_data.address,
            user_id=user.id
        )

        try:
            with db.begin_nested():
                db.add(new_contact)
                db.flush()
        except IntegrityError:
            summary["skipped_duplicates"] += 1
            continue

        seen_phones.add(contact_data.phone_number)
        if contact_data.email:
            seen_emails.add(contact_data.email)
        summary["imported"] += 1

    try:
        db.commit()
    except IntegrityError:
        db.rollback()
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail="The import could not be committed. No imported rows were saved."
        )

    return summary


# GET SINGLE CONTACT
@router.get(
    "/{contact_id}",
    response_model=ContactResponse
)
def get_contact(
    contact_id: int,
    db: Session = Depends(get_db),
    user: User = Depends(get_current_user)
):

    contact = (
        db.query(Contact)
        .filter(Contact.id == contact_id, Contact.user_id == user.id)
        .first()
    )

    if not contact:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Contact not found."
        )

    return contact


# UPDATE CONTACT
@router.put(
    "/{contact_id}",
    response_model=ContactResponse
)
def update_contact(
    contact_id: int,
    updated_contact: ContactUpdate,
    db: Session = Depends(get_db),
    user: User = Depends(get_current_user)
):

    contact = (
        db.query(Contact)
        .filter(Contact.id == contact_id, Contact.user_id == user.id)
        .first()
    )

    if not contact:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Contact not found."
        )

    # Only update fields that were actually sent
    update_data = updated_contact.model_dump(
        exclude_unset=True
    )

    # Check duplicate phone number
    if "phone_number" in update_data:

        existing_phone = (
            db.query(Contact)
            .filter(
                Contact.phone_number ==
                update_data["phone_number"],

                Contact.id != contact_id
            )
            .first()
        )

        if existing_phone:
            raise HTTPException(
                status_code=status.HTTP_409_CONFLICT,
                detail="This phone number already exists."
            )

    # Check duplicate email
    if (
        "email" in update_data
        and update_data["email"] is not None
    ):

        existing_email = (
            db.query(Contact)
            .filter(
                Contact.email ==
                update_data["email"],

                Contact.id != contact_id
            )
            .first()
        )

        if existing_email:
            raise HTTPException(
                status_code=status.HTTP_409_CONFLICT,
                detail="This email address already exists."
            )

    # Update fields
    for field, value in update_data.items():
        setattr(contact, field, value)

    try:
        db.commit()
        db.refresh(contact)

        return contact

    except IntegrityError:
        db.rollback()

        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="A contact with this phone number or email already exists."
        )


# DELETE CONTACT
@router.delete(
    "/{contact_id}"
)
def delete_contact(
    contact_id: int,
    db: Session = Depends(get_db),
    user: User = Depends(get_current_user)
):

    contact = (
        db.query(Contact)
        .filter(Contact.id == contact_id, Contact.user_id == user.id)
        .first()
    )

    if not contact:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Contact not found."
        )

    db.delete(contact)
    db.commit()

    return {
        "message": "Contact deleted successfully."
    }