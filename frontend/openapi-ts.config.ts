import { defineConfig } from '@hey-api/openapi-ts';

export default defineConfig({
    input: '../backend/webshell/openapi/webshell_backend.json',
    output: {
        path: 'src/lib/client',
        entryFile: false,
    },
    plugins: [
        {
            name: '@hey-api/sdk',
            operations: {
                containerName: 'BackendClient',
                strategy: 'single'
            }
        }
    ]
});