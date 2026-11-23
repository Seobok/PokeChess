"""Local-only Unity Web build server with compressed asset headers."""
import argparse
import http.server
from pathlib import Path


class UnityHandler(http.server.SimpleHTTPRequestHandler):
    def guess_type(self, path):
        raw = path.removesuffix('.br').removesuffix('.gz')
        if raw.endswith('.wasm'):
            return 'application/wasm'
        if raw.endswith('.js'):
            return 'application/javascript'
        return super().guess_type(raw)

    def end_headers(self):
        if self.path.endswith('.br'):
            self.send_header('Content-Encoding', 'br')
        elif self.path.endswith('.gz'):
            self.send_header('Content-Encoding', 'gzip')
        self.send_header('Cache-Control', 'no-store')
        super().end_headers()


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--port', type=int, default=8765)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[1] / 'Builds/WebMultiplayerQA')
    args = parser.parse_args()
    import functools
    handler = functools.partial(UnityHandler, directory=str(args.root.resolve()))
    http.server.ThreadingHTTPServer(('127.0.0.1', args.port), handler).serve_forever()
