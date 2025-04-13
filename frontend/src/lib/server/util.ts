import { randomBytes } from 'node:crypto';


export async function random_string(): Promise<string> {
    return new Promise((res, rej) => {
        randomBytes(32, (err, buf) => {
            if (err)
                rej(err);
            else
                res(buf.toString('base64url'))
        });
    });
}
