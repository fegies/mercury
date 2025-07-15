import type { RequestHandler } from "./$types";


async function delay(time: number) {
    return new Promise((res, rej) => {
        setTimeout(res, time);
    });
}

function get_stream(): ReadableStream {
    let remaining = 10;
    return new ReadableStream({
        async pull(controller) {
            console.log(remaining)
            if (remaining == 0)
                controller.close();

            await delay(1000);
            controller.enqueue(remaining-- + '\n');

        },
        cancel(reason) {
        },
    }, {

    })
}

export const GET: RequestHandler = async (event) => {

    console.log("r");
    return new Response(get_stream(), {
        status: 200,
        headers: {
            'Content-Type': 'text/plain',
        }
    });
};