import { Component, HostListener, Input, OnInit, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

type Route =
  | '/'
  | '/login'
  | '/register'
  | '/forgot-password'
  | '/reset-password'
  | '/force-password-change'
  | '/usuarios';

type SessionUser = {
  email: string;
  fullName: string;
  mustChangePassword: boolean;
  roles: string[];
};

type ManagedUser = {
  id: string;
  email: string;
  fullName: string;
  isApproved: boolean;
  isActive: boolean;
  mustChangePassword: boolean;
};

type Message = {
  text: string;
  isError?: boolean;
};

type PasswordKey = 'login' | 'register' | 'reset' | 'current' | 'new';

const publicRoutes = new Set<string>(['/login', '/register', '/forgot-password', '/reset-password']);

@Component({
  selector: 'app-message',
  template: `
    @if (message) {
      <p class="message visible" [class.error]="message.isError" role="status">{{ message.text }}</p>
    }
  `
})
export class MessageComponent {
  @Input()
  message: Message | null = null;
}

@Component({
  selector: 'app-root',
  imports: [FormsModule, MessageComponent],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App implements OnInit {
  readonly route = signal<Route>(this.readRoute());
  readonly session = signal<SessionUser | null>(null);
  readonly loadingSession = signal(true);
  readonly loadingUsers = signal(false);
  readonly submitting = signal(false);
  readonly message = signal<Message | null>(null);
  readonly users = signal<ManagedUser[]>([]);
  readonly ordersStep = signal<1 | 2>(1);
  readonly pastedCount = signal(0);
  readonly normalizedOrders = signal<string[]>([]);
  readonly passwordVisible = signal<Record<PasswordKey, boolean>>({
    login: false,
    register: false,
    reset: false,
    current: false,
    new: false
  });

  readonly isAdmin = computed(() => this.session()?.roles.includes('Admin') === true);

  loginForm = {
    email: '',
    password: '',
    rememberMe: false
  };

  registerForm = {
    fullName: '',
    email: '',
    password: ''
  };

  forgotPasswordEmail = '';

  resetForm = {
    email: new URLSearchParams(window.location.search).get('email') || '',
    token: new URLSearchParams(window.location.search).get('token') || '',
    password: ''
  };

  changeForm = {
    currentPassword: '',
    newPassword: ''
  };

  month = new Date().toISOString().slice(0, 7);
  ordersText = '';

  async ngOnInit() {
    await this.refreshSession();
  }

  @HostListener('window:popstate')
  onPopState() {
    this.route.set(this.readRoute());
    void this.applyRouteGuard();
  }

  async loginSubmit() {
    await this.withSubmit(async () => {
      const response = await this.postJson<{ mustChangePassword: boolean }>('/auth/login', this.loginForm);
      await this.refreshSession();
      this.go(response.mustChangePassword ? '/force-password-change' : '/');
    });
  }

  async registerSubmit() {
    await this.withSubmit(async () => {
      await this.postJson('/auth/register', this.registerForm);
      this.registerForm = { fullName: '', email: '', password: '' };
      this.message.set({ text: 'Solicitud enviada. Un administrador debe aprobar tu usuario.' });
    });
  }

  async forgotPasswordSubmit() {
    await this.withSubmit(async () => {
      await this.postJson('/auth/forgot-password', { email: this.forgotPasswordEmail });
      this.forgotPasswordEmail = '';
      this.message.set({ text: 'Si el usuario existe y está activo, recibirá un correo de reinicio.' });
    });
  }

  async resetPasswordSubmit() {
    await this.withSubmit(async () => {
      await this.postJson('/auth/reset-password', this.resetForm);
      this.message.set({ text: 'Contraseña actualizada. Ya puedes iniciar sesión.' });
      window.setTimeout(() => this.go('/login'), 1200);
    });
  }

  async changePasswordSubmit() {
    await this.withSubmit(async () => {
      await this.postJson('/auth/change-password', this.changeForm);
      await this.refreshSession();
      this.go('/');
    });
  }

  async logoutSubmit() {
    try {
      await fetch('/auth/logout', {
        method: 'POST',
        credentials: 'same-origin'
      });
    } finally {
      this.session.set(null);
      this.go('/login');
    }
  }

  downloadMonthlyReport() {
    window.location.href = `/reportes/mensual/download?month=${encodeURIComponent(this.month)}`;
  }

  cleanOrders() {
    const tokens = this.splitOrders(this.ordersText);
    const normalized = this.distinct(tokens);
    if (normalized.length === 0) {
      this.message.set({ text: 'Pega al menos un número de pedido.', isError: true });
      return;
    }

    this.message.set(null);
    this.pastedCount.set(tokens.length);
    this.normalizedOrders.set(normalized);
    this.ordersStep.set(2);
  }

  async downloadOrdersReport() {
    await this.withSubmit(async () => {
      const response = await this.postJson<Response>('/reportes/pedidos/download', {
        orderNumbers: this.normalizedOrders()
      }, true);

      const blob = await response.blob();
      this.downloadBlob(blob, this.getFileName(response.headers.get('content-disposition')) || 'reporte-eventos-pedidos.xlsx');
    });
  }

  async approveUserSubmit(id: string) {
    await this.withSubmit(async () => {
      await this.postJson(`/admin/users/${encodeURIComponent(id)}/approve`, {});
      await this.loadUsers();
    });
  }

  async setUserActiveSubmit(id: string, isActive: boolean) {
    await this.withSubmit(async () => {
      await this.postJson(`/admin/users/${encodeURIComponent(id)}/active`, { isActive });
      await this.loadUsers();
    });
  }

  togglePassword(key: PasswordKey) {
    this.passwordVisible.update((current) => ({
      ...current,
      [key]: !current[key]
    }));
  }

  go(route: Route) {
    window.history.pushState({}, '', route);
    this.route.set(route);
    this.message.set(null);
    void this.applyRouteGuard();
  }

  private async refreshSession() {
    this.loadingSession.set(true);
    try {
      const response = await fetch('/auth/me', {
        credentials: 'same-origin'
      });

      if (response.status === 401) {
        this.session.set(null);
        return;
      }

      await this.ensureOk(response, 'No se pudo validar la sesión.');
      this.session.set(await response.json());
    } finally {
      this.loadingSession.set(false);
      await this.applyRouteGuard();
    }
  }

  private async applyRouteGuard() {
    if (this.loadingSession()) {
      return;
    }

    if (!this.session() && !publicRoutes.has(this.route())) {
      this.go('/login');
      return;
    }

    if (this.session()?.mustChangePassword && this.route() !== '/force-password-change') {
      this.go('/force-password-change');
      return;
    }

    if (this.route() === '/usuarios' && this.isAdmin()) {
      await this.loadUsers();
    }
  }

  private async loadUsers() {
    this.loadingUsers.set(true);
    try {
      const response = await fetch('/admin/users', {
        credentials: 'same-origin'
      });
      await this.ensureOk(response, 'No se pudo cargar la lista de usuarios.');
      this.users.set(await response.json());
    } catch (error) {
      this.message.set(this.errorMessage(error));
    } finally {
      this.loadingUsers.set(false);
    }
  }

  private async withSubmit(action: () => Promise<void>) {
    this.submitting.set(true);
    this.message.set(null);
    try {
      await action();
    } catch (error) {
      this.message.set(this.errorMessage(error));
    } finally {
      this.submitting.set(false);
    }
  }

  private async postJson<T = unknown>(url: string, body: unknown, raw = false): Promise<T> {
    const response = await fetch(url, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    });

    await this.ensureOk(response, 'No se pudo completar la solicitud.');
    if (raw) {
      return response as T;
    }

    const text = await response.text();
    return text ? JSON.parse(text) : undefined as T;
  }

  private async ensureOk(response: Response, fallback: string) {
    if (response.ok) {
      return;
    }

    throw new Error(await this.readError(response, fallback));
  }

  private async readError(response: Response, fallback: string): Promise<string> {
    try {
      const body = await response.json();
      if (Array.isArray(body.errors) && body.errors.length > 0) {
        return body.errors.join(' ');
      }

      return body.detail || fallback;
    } catch {
      return fallback;
    }
  }

  private errorMessage(error: unknown): Message {
    return {
      text: error instanceof Error ? error.message : 'No se pudo completar la solicitud.',
      isError: true
    };
  }

  private readRoute(): Route {
    const allowedRoutes: Route[] = ['/', '/login', '/register', '/forgot-password', '/reset-password', '/force-password-change', '/usuarios'];
    return allowedRoutes.includes(window.location.pathname as Route) ? window.location.pathname as Route : '/';
  }

  private splitOrders(value: string): string[] {
    return value
      .split(/[\s,;]+/g)
      .map((item) => item.trim())
      .filter(Boolean);
  }

  private distinct(values: string[]): string[] {
    const seen = new Set<string>();
    const result: string[] = [];

    for (const value of values) {
      if (!seen.has(value)) {
        seen.add(value);
        result.push(value);
      }
    }

    return result;
  }

  private downloadBlob(blob: Blob, fileName: string) {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
  }

  private getFileName(contentDisposition: string | null): string | null {
    if (!contentDisposition) {
      return null;
    }

    const utf8Match = contentDisposition.match(/filename\*=UTF-8''([^;]+)/i);
    if (utf8Match) {
      return decodeURIComponent(utf8Match[1]);
    }

    const asciiMatch = contentDisposition.match(/filename="?([^"]+)"?/i);
    return asciiMatch ? asciiMatch[1] : null;
  }
}
