import { Component, HostListener, Input, OnDestroy, OnInit, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  LucideChartNoAxesColumn,
  LucideChevronDown,
  LucideCircleUserRound,
  LucideDownload,
  LucideDynamicIcon,
  LucideFileText,
  LucideFilter,
  LucideHome,
  LucideKeyRound,
  LucideLogOut,
  LucideMenu,
  LucidePanelLeftClose,
  LucidePanelLeftOpen,
  LucidePlus,
  LucideSearch,
  LucideSettings,
  LucideShieldCheck,
  LucideSlidersHorizontal,
  LucideArrowUpDown,
  LucideUserPen,
  LucideUsers
} from '@lucide/angular';
import type { LucideIconInput } from '@lucide/angular';

type Route =
  | '/'
  | '/login'
  | '/register'
  | '/forgot-password'
  | '/reset-password'
  | '/force-password-change'
  | '/reportes/eventos'
  | '/seguridad/usuarios'
  | '/preferencias'
  | '/sin-acceso';

type SessionUser = {
  email: string;
  fullName: string;
  mustChangePassword: boolean;
  roles: string[];
  permissions?: string[];
};

type RoleSummary = {
  id: string;
  name: string;
  displayName: string;
  displayOrder: number;
  concurrencyStamp: string;
  permissions?: string[];
};

type ManagedUser = {
  id: string;
  email: string;
  fullName: string;
  isApproved: boolean;
  isActive: boolean;
  mustChangePassword: boolean;
  concurrencyStamp: string;
  roles: RoleSummary[];
};

type UserEditForm = {
  id: string;
  email: string;
  fullName: string;
  isApproved: boolean;
  isActive: boolean;
  concurrencyStamp: string;
  roleNames: string[];
  isNew: boolean;
};

type RoleEditForm = {
  id: string;
  name: string;
  displayName: string;
  displayOrder: number;
  concurrencyStamp: string;
  permissions: string[];
  isNew: boolean;
};

type Message = {
  text: string;
  isError?: boolean;
};

type PasswordKey = 'login' | 'register' | 'reset' | 'current' | 'new';
type ThemePreference = 'system' | 'light' | 'dark';
type StartPreference = 'remember-last' | 'home' | 'first-module';
type UserApprovalFilter = 'all' | 'approved' | 'pending';
type UserStatusFilter = 'all' | 'active' | 'inactive';
type UserSortKey = 'fullName' | 'email' | 'status' | 'approval' | 'roles';
type SortDirection = 'asc' | 'desc';
type ModuleDefinition = {
  category: string;
  route: Route;
  title: string;
  description: string;
  permission: string;
  featured: boolean;
  icon: LucideIconInput;
};

const publicRoutes = new Set<Route>(['/login', '/register', '/forgot-password', '/reset-password']);
const nonStoredRoutes = new Set<Route>([...publicRoutes, '/force-password-change', '/preferencias', '/sin-acceso']);
const lastRouteStorageKey = 'rym.lastRoute';
const themeStorageKey = 'rym.theme';
const sidebarStorageKey = 'rym.sidebarCollapsed';
const startPreferenceStorageKey = 'rym.startPreference';

const Permissions = {
  PlatformHomeAccess: 'Platform.Home.Access',
  UsersManage: 'Users.Manage',
  ReportsEventsAccess: 'Reports.Events.Access',
  ReportsEventsDownload: 'Reports.Events.Download',
  PreferencesManageOwn: 'Preferences.ManageOwn'
} as const;

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
  imports: [FormsModule, MessageComponent, LucideDynamicIcon],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App implements OnInit, OnDestroy {
  readonly route = signal<Route>(this.readRoute());
  readonly session = signal<SessionUser | null>(null);
  readonly theme = signal<ThemePreference>(this.readThemePreference());
  readonly systemPrefersDark = signal(window.matchMedia('(prefers-color-scheme: dark)').matches);
  readonly loadingSession = signal(true);
  readonly loadingUsers = signal(false);
  readonly submitting = signal(false);
  readonly message = signal<Message | null>(null);
  readonly users = signal<ManagedUser[]>([]);
  readonly roles = signal<RoleSummary[]>([]);
  readonly allPermissions = signal<string[]>([]);
  readonly selectedUser = signal<UserEditForm | null>(null);
  readonly selectedRole = signal<RoleEditForm | null>(null);
  readonly roleSearch = signal('');
  readonly permissionSearch = signal('');
  readonly userSearch = signal('');
  readonly userApprovalFilter = signal<UserApprovalFilter>('all');
  readonly userStatusFilter = signal<UserStatusFilter>('all');
  readonly userSortKey = signal<UserSortKey>('email');
  readonly userSortDirection = signal<SortDirection>('asc');
  readonly userPage = signal(1);
  readonly userPageSize = signal(10);
  readonly createdTemporaryPassword = signal<string | null>(null);
  readonly mobileMenuOpen = signal(false);
  readonly sidebarCollapsed = signal(this.readSidebarCollapsed());
  readonly accountMenuOpen = signal(false);
  readonly startPreference = signal<StartPreference>(this.readStartPreference());
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

  readonly effectiveTheme = computed(() => this.theme() === 'system'
    ? this.systemPrefersDark() ? 'dark' : 'light'
    : this.theme());
  readonly logoSrc = computed(() => this.effectiveTheme() === 'dark'
    ? '/logo-remesas-mensajes.svg'
    : '/logo-remesas-mensajes.png');

  readonly icons = {
    chart: LucideChartNoAxesColumn,
    chevronDown: LucideChevronDown,
    closePanel: LucidePanelLeftClose,
    download: LucideDownload,
    editUser: LucideUserPen,
    fileText: LucideFileText,
    filter: LucideFilter,
    home: LucideHome,
    key: LucideKeyRound,
    logOut: LucideLogOut,
    menu: LucideMenu,
    openPanel: LucidePanelLeftOpen,
    plus: LucidePlus,
    search: LucideSearch,
    settings: LucideSettings,
    shield: LucideShieldCheck,
    sliders: LucideSlidersHorizontal,
    sort: LucideArrowUpDown,
    user: LucideCircleUserRound,
    users: LucideUsers
  };

  readonly modules: ModuleDefinition[] = [
    {
      category: 'Reportes',
      route: '/reportes/eventos' as Route,
      title: 'Reporte de eventos',
      description: 'Descarga reportes mensuales o por lista de pedidos.',
      permission: Permissions.ReportsEventsAccess,
      featured: true,
      icon: LucideFileText
    },
    {
      category: 'Seguridad',
      route: '/seguridad/usuarios' as Route,
      title: 'Usuarios',
      description: 'Administra aprobación, estado y roles de acceso.',
      permission: Permissions.UsersManage,
      featured: true,
      icon: LucideUsers
    },
    {
      category: 'Configuración',
      route: '/preferencias' as Route,
      title: 'Preferencias',
      description: 'Tema visual y preferencias de navegación.',
      permission: Permissions.PreferencesManageOwn,
      featured: false,
      icon: LucideSettings
    }
  ];
  readonly visibleModules = computed(() => this.modules.filter((module) => this.hasPermission(module.permission)));
  readonly featuredModules = computed(() => this.visibleModules().filter((module) => module.featured));
  readonly navigationSections = computed(() => {
    const categories = ['Reportes', 'Seguridad', 'Configuración'];
    return categories
      .map((category) => ({
        category,
        items: this.modules.filter((module) => module.category === category && this.hasPermission(module.permission))
      }))
      .filter((section) => section.items.length > 0);
  });
  readonly filteredRoles = computed(() => {
    const search = this.roleSearch().trim().toLowerCase();
    return this.roles().filter((role) =>
      !search
      || role.displayName.toLowerCase().includes(search)
      || role.name.toLowerCase().includes(search));
  });
  readonly filteredPermissions = computed(() => {
    const search = this.permissionSearch().trim().toLowerCase();
    return this.allPermissions().filter((permission) =>
      !search
      || permission.toLowerCase().includes(search)
      || this.permissionLabel(permission).toLowerCase().includes(search));
  });
  readonly filteredUsers = computed(() => {
    const search = this.userSearch().trim().toLowerCase();
    const approval = this.userApprovalFilter();
    const status = this.userStatusFilter();

    return this.users().filter((user) => {
      const roles = this.roleDisplayText(user).toLowerCase();
      const matchesSearch = !search
        || user.fullName.toLowerCase().includes(search)
        || user.email.toLowerCase().includes(search)
        || roles.includes(search);
      const matchesApproval = approval === 'all'
        || (approval === 'approved' ? user.isApproved : !user.isApproved);
      const matchesStatus = status === 'all'
        || (status === 'active' ? user.isActive : !user.isActive);

      return matchesSearch && matchesApproval && matchesStatus;
    });
  });
  readonly sortedUsers = computed(() => {
    const key = this.userSortKey();
    const direction = this.userSortDirection() === 'asc' ? 1 : -1;
    return [...this.filteredUsers()].sort((a, b) => direction * this.compareUsers(a, b, key));
  });
  readonly totalUserPages = computed(() => Math.max(1, Math.ceil(this.sortedUsers().length / this.userPageSize())));
  readonly pagedUsers = computed(() => {
    const currentPage = Math.min(this.userPage(), this.totalUserPages());
    const start = (currentPage - 1) * this.userPageSize();
    return this.sortedUsers().slice(start, start + this.userPageSize());
  });
  readonly userPageStart = computed(() => this.sortedUsers().length === 0 ? 0 : ((Math.min(this.userPage(), this.totalUserPages()) - 1) * this.userPageSize()) + 1);
  readonly userPageEnd = computed(() => Math.min(this.userPageStart() + this.pagedUsers().length - 1, this.sortedUsers().length));

  private readonly colorSchemeMedia = window.matchMedia('(prefers-color-scheme: dark)');
  private readonly colorSchemeListener = (event: MediaQueryListEvent) => this.systemPrefersDark.set(event.matches);

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
    this.applyThemePreference(this.theme());
    this.colorSchemeMedia.addEventListener('change', this.colorSchemeListener);
    if (publicRoutes.has(this.route())) {
      this.loadingSession.set(false);
      return;
    }

    await this.refreshSession();
  }

  ngOnDestroy() {
    this.colorSchemeMedia.removeEventListener('change', this.colorSchemeListener);
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
      this.go(response.mustChangePassword ? '/force-password-change' : this.getInitialPrivateRoute());
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
      this.go(this.getInitialPrivateRoute());
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

  async openEditUser(user: ManagedUser) {
    if (this.roles().length === 0) {
      await this.loadRoles();
    }

    this.roleSearch.set('');
    this.createdTemporaryPassword.set(null);
    this.selectedUser.set({
      id: user.id,
      email: user.email,
      fullName: user.fullName,
      isApproved: user.isApproved,
      isActive: user.isActive,
      concurrencyStamp: user.concurrencyStamp,
      roleNames: user.roles.map((role) => role.name),
      isNew: false
    });
  }

  async openNewUser() {
    if (this.roles().length === 0) {
      await this.loadRoles();
    }

    this.roleSearch.set('');
    this.createdTemporaryPassword.set(null);
    this.selectedUser.set({
      id: '',
      email: '',
      fullName: '',
      isApproved: true,
      isActive: true,
      concurrencyStamp: '',
      roleNames: ['User'],
      isNew: true
    });
  }

  closeEditUser() {
    this.selectedUser.set(null);
    this.roleSearch.set('');
    this.createdTemporaryPassword.set(null);
  }

  async openNewRole() {
    if (this.allPermissions().length === 0) {
      await this.loadPermissions();
    }

    this.permissionSearch.set('');
    this.selectedRole.set({
      id: '',
      name: '',
      displayName: '',
      displayOrder: 10,
      concurrencyStamp: '',
      permissions: [],
      isNew: true
    });
  }

  async openEditRole(role: RoleSummary) {
    if (this.allPermissions().length === 0) {
      await this.loadPermissions();
    }

    this.permissionSearch.set('');
    this.selectedRole.set({
      id: role.id,
      name: role.name,
      displayName: role.displayName,
      displayOrder: role.displayOrder,
      concurrencyStamp: role.concurrencyStamp,
      permissions: [...(role.permissions || [])],
      isNew: false
    });
  }

  closeEditRole() {
    this.selectedRole.set(null);
    this.permissionSearch.set('');
  }

  toggleEditedRole(roleName: string, checked: boolean) {
    this.selectedUser.update((user) => {
      if (!user) {
        return user;
      }

      const roleNames = new Set(user.roleNames);
      checked ? roleNames.add(roleName) : roleNames.delete(roleName);
      return {
        ...user,
        roleNames: Array.from(roleNames)
      };
    });
  }

  updateSelectedUserField<K extends keyof UserEditForm>(field: K, value: UserEditForm[K]) {
    this.selectedUser.update((user) => user ? { ...user, [field]: value } : user);
  }

  updateSelectedRoleField<K extends keyof RoleEditForm>(field: K, value: RoleEditForm[K]) {
    this.selectedRole.update((role) => role ? { ...role, [field]: value } : role);
  }

  toggleEditedPermission(permission: string, checked: boolean) {
    this.selectedRole.update((role) => {
      if (!role) {
        return role;
      }

      const permissions = new Set(role.permissions);
      checked ? permissions.add(permission) : permissions.delete(permission);
      return {
        ...role,
        permissions: Array.from(permissions).sort((a, b) => a.localeCompare(b, 'es', { sensitivity: 'base' }))
      };
    });
  }

  roleDisplayText(user: ManagedUser): string {
    return user.roles.map((role) => role.displayName).join(', ') || 'Sin rol';
  }

  permissionDisplayText(role: RoleSummary): string {
    return (role.permissions || []).map((permission) => this.permissionLabel(permission)).join(', ') || 'Sin funcionalidades';
  }

  async saveEditedUser() {
    const user = this.selectedUser();
    if (!user) {
      return;
    }

    await this.withSubmit(async () => {
      if (user.isNew) {
        const created = await this.postJson<{ temporaryPassword: string }>('/admin/users', user);
        this.createdTemporaryPassword.set(created.temporaryPassword);
        this.message.set({ text: 'Usuario creado. Entrega la contraseña temporal al usuario por un canal seguro.' });
      } else {
        await this.putJson(`/admin/users/${encodeURIComponent(user.id)}`, user);
        this.message.set({ text: 'Usuario actualizado.' });
        this.closeEditUser();
      }

      await this.loadUsers();
    });
  }

  async saveEditedRole() {
    const role = this.selectedRole();
    if (!role) {
      return;
    }

    await this.withSubmit(async () => {
      if (role.isNew) {
        await this.postJson('/admin/roles', role);
        this.message.set({ text: 'Rol creado.' });
      } else {
        await this.putJson(`/admin/roles/${encodeURIComponent(role.id)}`, role);
        this.message.set({ text: 'Rol actualizado.' });
      }

      this.closeEditRole();
      await Promise.all([this.loadRoles(), this.loadUsers()]);
    });
  }

  async deleteRoleSubmit(role: RoleSummary) {
    const accepted = window.confirm(`Se eliminará el rol ${role.displayName}. ¿Deseas continuar?`);
    if (!accepted) {
      return;
    }

    await this.withSubmit(async () => {
      await this.deleteJson(`/admin/roles/${encodeURIComponent(role.id)}`);
      this.message.set({ text: 'Rol eliminado.' });
      await Promise.all([this.loadRoles(), this.loadUsers()]);
    });
  }

  async deactivateUserSubmit(user: ManagedUser) {
    const accepted = window.confirm(`Se desactivará el acceso de ${user.email}. ¿Deseas continuar?`);
    if (!accepted) {
      return;
    }

    await this.withSubmit(async () => {
      await this.postJson(`/admin/users/${encodeURIComponent(user.id)}/deactivate`, {});
      this.message.set({ text: 'Acceso desactivado.' });
      await this.loadUsers();
    });
  }

  togglePassword(key: PasswordKey) {
    this.passwordVisible.update((current) => ({
      ...current,
      [key]: !current[key]
    }));
  }

  setTheme(theme: ThemePreference) {
    this.theme.set(theme);
    localStorage.setItem(themeStorageKey, theme);
    this.applyThemePreference(theme);
  }

  setStartPreference(preference: StartPreference) {
    this.startPreference.set(preference);
    localStorage.setItem(startPreferenceStorageKey, preference);
    if (preference !== 'remember-last') {
      this.forgetLastRoute();
    }
  }

  toggleSidebar() {
    const nextValue = !this.sidebarCollapsed();
    this.sidebarCollapsed.set(nextValue);
    localStorage.setItem(sidebarStorageKey, String(nextValue));
  }

  updateUserSearch(value: string) {
    this.userSearch.set(value);
    this.userPage.set(1);
  }

  updateUserApprovalFilter(value: UserApprovalFilter) {
    this.userApprovalFilter.set(value);
    this.userPage.set(1);
  }

  updateUserStatusFilter(value: UserStatusFilter) {
    this.userStatusFilter.set(value);
    this.userPage.set(1);
  }

  updateUserPageSize(value: string | number) {
    this.userPageSize.set(Number(value));
    this.userPage.set(1);
  }

  sortUsers(key: UserSortKey) {
    if (this.userSortKey() === key) {
      this.userSortDirection.set(this.userSortDirection() === 'asc' ? 'desc' : 'asc');
      return;
    }

    this.userSortKey.set(key);
    this.userSortDirection.set('asc');
  }

  goToUserPage(page: number) {
    this.userPage.set(Math.max(1, Math.min(page, this.totalUserPages())));
  }

  resetUserFilters() {
    this.userSearch.set('');
    this.userApprovalFilter.set('all');
    this.userStatusFilter.set('all');
    this.userPage.set(1);
  }

  exportUsers() {
    const rows = this.sortedUsers();
    const headers = ['Nombre', 'Email', 'Estado', 'Aprobacion', 'Roles'];
    const csvRows = [
      headers,
      ...rows.map((user) => [
        user.fullName,
        user.email,
        user.isActive ? 'Activo' : 'Inactivo',
        user.isApproved ? 'Aprobado' : 'Pendiente',
        this.roleDisplayText(user)
      ])
    ];
    const csv = csvRows
      .map((row) => row.map((value) => `"${String(value).replaceAll('"', '""')}"`).join(','))
      .join('\n');
    const fileName = `usuarios-rym-${new Date().toISOString().slice(0, 10).replaceAll('-', '')}.csv`;
    this.downloadBlob(new Blob([csv], { type: 'text/csv;charset=utf-8' }), fileName);
  }

  permissionLabel(permission: string): string {
    const labels: Record<string, string> = {
      [Permissions.PlatformHomeAccess]: 'Ver inicio',
      [Permissions.UsersManage]: 'Administrar usuarios, roles y permisos',
      [Permissions.ReportsEventsAccess]: 'Ver reporte de eventos',
      [Permissions.ReportsEventsDownload]: 'Descargar reportes de eventos',
      [Permissions.PreferencesManageOwn]: 'Gestionar preferencias propias'
    };
    return labels[permission] || permission;
  }

  hasPermission(permission: string) {
    const session = this.session();
    if (!session) {
      return false;
    }

    if (session.permissions?.includes(permission)) {
      return true;
    }

    if (session.roles.includes('Admin')) {
      return true;
    }

    return session.roles.includes('User')
      && (permission === Permissions.ReportsEventsAccess
        || permission === Permissions.ReportsEventsDownload
        || permission === Permissions.PreferencesManageOwn);
  }

  go(route: Route) {
    window.history.pushState({}, '', route);
    this.route.set(route);
    this.message.set(null);
    this.mobileMenuOpen.set(false);
    this.accountMenuOpen.set(false);
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

    if (!this.session()) {
      return;
    }

    if (this.session()?.mustChangePassword && this.route() !== '/force-password-change') {
      this.go('/force-password-change');
      return;
    }

    if (!this.canAccessRoute(this.route())) {
      this.forgetLastRoute();
      const fallbackRoute = this.getFallbackRoute();
      if (this.route() !== fallbackRoute) {
        this.go(fallbackRoute);
      }
      return;
    }

    this.rememberLastRoute(this.route());

    if (this.route() === '/seguridad/usuarios' && this.hasPermission(Permissions.UsersManage)) {
      await Promise.all([this.loadUsers(), this.loadRoles(), this.loadPermissions()]);
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

  private async loadRoles() {
    const response = await fetch('/admin/roles', {
      credentials: 'same-origin'
    });
    await this.ensureOk(response, 'No se pudo cargar la lista de roles.');
    this.roles.set(await response.json());
  }

  private async loadPermissions() {
    const response = await fetch('/admin/permissions', {
      credentials: 'same-origin'
    });
    await this.ensureOk(response, 'No se pudo cargar la lista de permisos.');
    this.allPermissions.set(await response.json());
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

  private async putJson<T = unknown>(url: string, body: unknown): Promise<T> {
    const response = await fetch(url, {
      method: 'PUT',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    });

    await this.ensureOk(response, 'No se pudo completar la solicitud.');
    const text = await response.text();
    return text ? JSON.parse(text) : undefined as T;
  }

  private async deleteJson<T = unknown>(url: string): Promise<T> {
    const response = await fetch(url, {
      method: 'DELETE',
      credentials: 'same-origin'
    });

    await this.ensureOk(response, 'No se pudo completar la solicitud.');
    const text = await response.text();
    return text ? JSON.parse(text) : undefined as T;
  }

  private async ensureOk(response: Response, fallback: string) {
    if (response.ok) {
      return;
    }

    if (response.status === 401) {
      this.session.set(null);
      this.go('/login');
    }

    if (response.status === 403) {
      this.forgetLastRoute();
      this.go(this.getFallbackRoute());
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
    const allowedRoutes: Route[] = [
      '/',
      '/login',
      '/register',
      '/forgot-password',
      '/reset-password',
      '/force-password-change',
      '/reportes/eventos',
      '/seguridad/usuarios',
      '/preferencias',
      '/sin-acceso'
    ];
    return allowedRoutes.includes(window.location.pathname as Route) ? window.location.pathname as Route : '/';
  }

  private getInitialPrivateRoute(): Route {
    if (this.startPreference() === 'home') {
      return this.getFallbackRoute();
    }

    if (this.startPreference() === 'first-module') {
      return this.visibleModules().find((module) => module.route !== '/preferencias')?.route || this.getFallbackRoute();
    }

    const storedRoute = this.readLastRoute();
    if (storedRoute && this.canAccessRoute(storedRoute)) {
      return storedRoute;
    }

    return this.getFallbackRoute();
  }

  private getFallbackRoute(): Route {
    if (this.canAccessRoute('/')) {
      return '/';
    }

    const firstModule = this.visibleModules().find((module) => module.route !== '/preferencias');
    return firstModule?.route || '/sin-acceso';
  }

  private canAccessRoute(route: Route): boolean {
    if (publicRoutes.has(route) || route === '/force-password-change' || route === '/sin-acceso') {
      return true;
    }

    if (route === '/') {
      return this.hasPermission(Permissions.PlatformHomeAccess);
    }

    if (route === '/seguridad/usuarios') {
      return this.hasPermission(Permissions.UsersManage);
    }

    if (route === '/reportes/eventos') {
      return this.hasPermission(Permissions.ReportsEventsAccess);
    }

    if (route === '/preferencias') {
      return this.hasPermission(Permissions.PreferencesManageOwn);
    }

    return false;
  }

  private rememberLastRoute(route: Route) {
    if (!this.session() || this.startPreference() !== 'remember-last' || nonStoredRoutes.has(route)) {
      return;
    }

    localStorage.setItem(lastRouteStorageKey, route);
  }

  private readLastRoute(): Route | null {
    const value = localStorage.getItem(lastRouteStorageKey);
    const allowedRoutes: Route[] = ['/', '/reportes/eventos', '/seguridad/usuarios'];
    return allowedRoutes.includes(value as Route) ? value as Route : null;
  }

  private forgetLastRoute() {
    localStorage.removeItem(lastRouteStorageKey);
  }

  private readThemePreference(): ThemePreference {
    const value = localStorage.getItem(themeStorageKey);
    return value === 'light' || value === 'dark' || value === 'system' ? value : 'system';
  }

  private readStartPreference(): StartPreference {
    const value = localStorage.getItem(startPreferenceStorageKey);
    return value === 'home' || value === 'first-module' || value === 'remember-last'
      ? value
      : 'remember-last';
  }

  private readSidebarCollapsed(): boolean {
    return localStorage.getItem(sidebarStorageKey) === 'true';
  }

  private compareUsers(a: ManagedUser, b: ManagedUser, key: UserSortKey): number {
    if (key === 'status') {
      return Number(b.isActive) - Number(a.isActive);
    }

    if (key === 'approval') {
      return Number(b.isApproved) - Number(a.isApproved);
    }

    const valueA = key === 'roles' ? this.roleDisplayText(a) : a[key];
    const valueB = key === 'roles' ? this.roleDisplayText(b) : b[key];
    return valueA.localeCompare(valueB, 'es', { sensitivity: 'base' });
  }

  private applyThemePreference(theme: ThemePreference) {
    document.documentElement.dataset['theme'] = theme;
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
