import { test, expect } from "@playwright/test";
import { createTestUser, login } from "./helpers";

test.describe("Authentication screen", () => {
  test("registration creates an account and opens the phonebook", async ({ page }) => {
    const suffix = `${Date.now()}_${Math.random().toString(36).slice(2, 8)}`;
    const credentials = {
      username: `register_${suffix}`,
      email: `register_${suffix}@example.com`,
      password: "StrongPass123!"
    };

    await page.goto("/");
    await page.getByRole("button", { name: "Create an account" }).click();
    await expect(page.getByRole("heading", { name: "Create your account" })).toBeVisible();
    await expect(page.getByLabel("Username")).toBeVisible();
    await expect(page.getByLabel("Email")).toBeVisible();
    await expect(page.getByLabel("Password")).toBeVisible();

    await page.getByLabel("Username").fill(credentials.username);
    await page.getByLabel("Email").fill(credentials.email);
    await page.getByLabel("Password").fill(credentials.password);
    await page.getByRole("button", { name: "Register" }).click();

    await expect(page.getByRole("heading", { name: "Phonebook", exact: true })).toBeVisible();
  });

  test("valid login opens the protected phonebook", async ({ page }) => {
    const { api, credentials } = await createTestUser("login");
    await login(page, credentials);
    await expect(page.getByRole("button", { name: "Log out" })).toBeVisible();
    await api.dispose();
  });

  test("invalid login shows an error and unauthenticated users see no contacts", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Welcome back" })).toBeVisible();
    await page.getByLabel("Username or email").fill("does-not-exist");
    await page.getByLabel("Password").fill("wrong-password");
    await page.getByRole("button", { name: "Sign in" }).click();

    await expect(page.locator(".form-error")).toContainText("Invalid username/email or password");
    await expect(page.getByRole("heading", { name: "Welcome back" })).toBeVisible();
  });

  test("logout returns to the login screen", async ({ page }) => {
    const { api, credentials } = await createTestUser("logout");
    await login(page, credentials);
    await page.getByRole("button", { name: "Log out" }).click();
    await expect(page.getByRole("heading", { name: "Welcome back" })).toBeVisible();
    await api.dispose();
  });
});
