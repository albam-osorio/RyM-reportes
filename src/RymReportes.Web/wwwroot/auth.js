const form = document.querySelector("form[data-auth-form]");
const message = document.querySelector("#message");
const logoutButton = document.querySelector("#logout-button");

if (form) {
  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    clearMessage();

    const submitButton = form.querySelector("button[type='submit']");
    submitButton.disabled = true;

    try {
      await handleForm(form);
    } catch (error) {
      showMessage(error.message || "No se pudo completar la solicitud.", true);
    } finally {
      submitButton.disabled = false;
    }
  });
}

if (logoutButton) {
  logoutButton.addEventListener("click", async () => {
    await fetch("/auth/logout", { method: "POST" });
    window.location.href = "/login.html";
  });
}

if (document.body.dataset.page === "reset-password") {
  const params = new URLSearchParams(window.location.search);
  document.querySelector("#email").value = params.get("email") || "";
  document.querySelector("#token").value = params.get("token") || "";
}

if (document.body.dataset.page === "admin-users") {
  loadUsers();
}

async function handleForm(currentForm) {
  const action = currentForm.dataset.authForm;
  if (action === "register") {
    const response = await postJson("/auth/register", {
      fullName: field("fullName"),
      email: field("email"),
      password: field("password")
    });
    await ensureOk(response, "No se pudo registrar el usuario.");
    showMessage("Solicitud enviada. Un administrador debe aprobar tu usuario.");
    currentForm.reset();
    return;
  }

  if (action === "login") {
    const response = await postJson("/auth/login", {
      email: field("email"),
      password: field("password"),
      rememberMe: document.querySelector("#rememberMe")?.checked === true
    });
    await ensureOk(response, "Email o contrasena invalidos.");
    const body = await response.json();
    window.location.href = body.mustChangePassword ? "/force-password-change.html" : "/";
    return;
  }

  if (action === "forgot-password") {
    const response = await postJson("/auth/forgot-password", {
      email: field("email")
    });
    await ensureOk(response, "No se pudo enviar el correo.");
    showMessage("Si el usuario existe y esta activo, recibira un correo de reinicio.");
    currentForm.reset();
    return;
  }

  if (action === "reset-password") {
    const response = await postJson("/auth/reset-password", {
      email: field("email"),
      token: field("token"),
      password: field("password")
    });
    await ensureOk(response, "No se pudo cambiar la contrasena.");
    showMessage("Contrasena actualizada. Ya puedes iniciar sesion.");
    setTimeout(() => {
      window.location.href = "/login.html";
    }, 1200);
    return;
  }

  if (action === "change-password") {
    const response = await postJson("/auth/change-password", {
      currentPassword: field("currentPassword"),
      newPassword: field("newPassword")
    });
    await ensureOk(response, "No se pudo cambiar la contrasena.");
    window.location.href = "/";
  }
}

async function loadUsers() {
  const tbody = document.querySelector("#users-body");
  try {
    const response = await fetch("/admin/users");
    if (response.status === 401 || response.status === 403) {
      window.location.href = "/login.html";
      return;
    }

    await ensureOk(response, "No se pudo cargar la lista de usuarios.");
    const users = await response.json();
    tbody.innerHTML = "";

    for (const user of users) {
      const row = document.createElement("tr");
      row.innerHTML = `
        <td>${escapeHtml(user.fullName || "")}</td>
        <td>${escapeHtml(user.email || "")}</td>
        <td>${user.isApproved ? "Aprobado" : "Pendiente"}</td>
        <td>${user.isActive ? "Activo" : "Inactivo"}</td>
        <td class="row-actions"></td>
      `;

      const actions = row.querySelector(".row-actions");
      if (!user.isApproved) {
        actions.appendChild(actionButton("Aprobar", () => approveUser(user.id)));
      }

      actions.appendChild(actionButton(user.isActive ? "Desactivar" : "Activar", () => setUserActive(user.id, !user.isActive)));
      tbody.appendChild(row);
    }
  } catch (error) {
    showMessage(error.message || "No se pudo cargar la lista de usuarios.", true);
  }
}

async function approveUser(id) {
  const response = await fetch(`/admin/users/${encodeURIComponent(id)}/approve`, { method: "POST" });
  await ensureOk(response, "No se pudo aprobar el usuario.");
  await loadUsers();
}

async function setUserActive(id, isActive) {
  const response = await postJson(`/admin/users/${encodeURIComponent(id)}/active`, { isActive });
  await ensureOk(response, "No se pudo actualizar el usuario.");
  await loadUsers();
}

function actionButton(text, onClick) {
  const button = document.createElement("button");
  button.type = "button";
  button.textContent = text;
  button.addEventListener("click", onClick);
  return button;
}

function field(name) {
  return document.querySelector(`[name='${name}']`)?.value.trim() || "";
}

async function postJson(url, body) {
  return fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });
}

async function ensureOk(response, fallback) {
  if (response.ok) {
    return;
  }

  try {
    const body = await response.json();
    if (Array.isArray(body.errors) && body.errors.length > 0) {
      throw new Error(body.errors.join(" "));
    }

    throw new Error(body.detail || fallback);
  } catch (error) {
    if (error instanceof Error && error.message) {
      throw error;
    }

    throw new Error(fallback);
  }
}

function showMessage(text, isError = false) {
  message.textContent = text;
  message.classList.toggle("error", isError);
  message.classList.add("visible");
}

function clearMessage() {
  message.textContent = "";
  message.classList.remove("visible", "error");
}

function escapeHtml(value) {
  return value.replace(/[&<>"']/g, (character) => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    "\"": "&quot;",
    "'": "&#039;"
  })[character]);
}
