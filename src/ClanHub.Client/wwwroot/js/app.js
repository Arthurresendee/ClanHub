window.clanhub = {
    isVisible: () => document.visibilityState === 'visible',
    getTheme: () => { try { return localStorage.getItem('clanhub.theme'); } catch { return null; } },
    setTheme: (v) => { try { localStorage.setItem('clanhub.theme', v); } catch { } },
    prefersDark: () => window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches,
    copy: (text) => navigator.clipboard.writeText(text),
    isStandalone: () => window.matchMedia("(display-mode: standalone)").matches || window.navigator.standalone === true,
    isIos: () => /iphone|ipad|ipod/i.test(navigator.userAgent),
    canInstall: () => !!window.__clanhubInstallEvent,
    install: async () => {
        const e = window.__clanhubInstallEvent;
        if (!e) return false;
        e.prompt();
        const choice = await e.userChoice;
        window.__clanhubInstallEvent = null;
        return choice.outcome === "accepted";
    },
    isOnline: () => navigator.onLine,
    onConnectivity: (ref) => {
        const notify = () => ref.invokeMethodAsync("OnConnectivityChanged", navigator.onLine);
        window.addEventListener("online", notify);
        window.addEventListener("offline", notify);
    },
    onInstallAvailable: (ref) => {
        window.addEventListener("beforeinstallprompt", (e) => {
            e.preventDefault();
            window.__clanhubInstallEvent = e;
            ref.invokeMethodAsync("OnInstallAvailable");
        });
        window.addEventListener("appinstalled", () => { window.__clanhubInstallEvent = null; ref.invokeMethodAsync("OnInstalled"); });
    },
};
// beforeinstallprompt pode disparar antes do Blazor iniciar: guardamos o evento cedo.
window.addEventListener("beforeinstallprompt", (e) => { e.preventDefault(); window.__clanhubInstallEvent = e; });