"""Start exactly one loopback worker; token is read only from a private file."""

import uvicorn

if __name__ == "__main__":
    uvicorn.run(
        "app.unity_preview:create_app_from_environment",
        factory=True,
        host="127.0.0.1",
        port=8000,
        workers=1,
        access_log=False,
    )
