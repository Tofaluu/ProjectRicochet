"""Serves Builds/WebGL on http://localhost:<port> with the headers Caddy adds on the cloud server (Unity's
files are pre-compressed). Lets the browser build talk to a local game server: an https page can't reach
http://localhost, a localhost page can. Usage: python Tools/serve-web.py [port] [host]"""
import functools
import http.server
import os
import sys

TYPES = {'.wasm': 'application/wasm', '.js': 'application/javascript', '.data': 'application/octet-stream'}


class Handler(http.server.SimpleHTTPRequestHandler):
    def guess_type(self, path):
        for ext, encoding in (('.gz', 'gzip'), ('.br', 'br')):
            if path.endswith(ext):
                inner = os.path.splitext(path[:-len(ext)])[1]
                return TYPES.get(inner, 'application/octet-stream')
        return super().guess_type(path)

    def end_headers(self):
        path = self.path.split('?')[0]
        if path.endswith('.gz'):
            self.send_header('Content-Encoding', 'gzip')
        elif path.endswith('.br'):
            self.send_header('Content-Encoding', 'br')
        self.send_header('Cache-Control', 'no-cache')
        super().end_headers()

    def log_message(self, format, *args):
        pass  # quiet


if __name__ == '__main__':
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8080
    # A second argument picks the address to listen on: 0.0.0.0 lets phones on the same Wi-Fi in (dev-session.sh --lan).
    host = sys.argv[2] if len(sys.argv) > 2 else 'localhost'
    root = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Builds', 'WebGL')
    server = http.server.ThreadingHTTPServer((host, port), functools.partial(Handler, directory=root))
    print(f'Serving {os.path.normpath(root)} at http://{host}:{port}')
    server.serve_forever()
