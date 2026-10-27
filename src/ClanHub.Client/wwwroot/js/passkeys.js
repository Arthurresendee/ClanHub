// WebAuthn (passkeys): o servidor entrega as opções em JSON e recebe o resultado em JSON.
window.clanhubPasskeys = {
    supported: () => !!(window.PublicKeyCredential
        && PublicKeyCredential.parseCreationOptionsFromJSON && PublicKeyCredential.parseRequestOptionsFromJSON),
    create: async (optionsJson) => {
        const options = PublicKeyCredential.parseCreationOptionsFromJSON(JSON.parse(optionsJson));
        const credential = await navigator.credentials.create({ publicKey: options });
        return JSON.stringify(credential.toJSON());
    },
    get: async (optionsJson) => {
        const options = PublicKeyCredential.parseRequestOptionsFromJSON(JSON.parse(optionsJson));
        const credential = await navigator.credentials.get({ publicKey: options });
        return JSON.stringify(credential.toJSON());
    },
};