"""Explicit local-only SSH smoke fixture. Requires Paramiko in artifacts/ssh-spike/python."""
import pathlib, sys, socket, threading, logging, json
root = pathlib.Path(__file__).resolve().parents[1] / "artifacts" / "ssh-spike"
sys.path.insert(0, str(root / "python"))
import paramiko
logging.getLogger("paramiko").addHandler(logging.NullHandler())
host = paramiko.RSAKey.generate(2048)
changed = paramiko.RSAKey.generate(2048)
client = paramiko.RSAKey.generate(2048)
client.write_private_key_file(str(root / "fixture-key"), password="fixture-passphrase")
for name in ("stop", "changed"):
    (root / name).unlink(missing_ok=True)
class Server(paramiko.ServerInterface):
    def __init__(self):
        self.ready = threading.Event()
        self.size = (100, 30)
    def check_auth_password(self, username, password):
        return paramiko.AUTH_SUCCESSFUL if username == "fixture" and password == "fixture-password" else paramiko.AUTH_FAILED
    def check_auth_publickey(self, username, key):
        return paramiko.AUTH_SUCCESSFUL if username == "fixture" and key == client else paramiko.AUTH_FAILED
    def get_allowed_auths(self, username): return "password,publickey"
    def check_channel_request(self, kind, channel): return paramiko.OPEN_SUCCEEDED if kind == "session" else paramiko.OPEN_FAILED_ADMINISTRATIVELY_PROHIBITED
    def check_channel_pty_request(self, channel, term, width, height, *args):
        self.size = (width, height)
        return True
    def check_channel_window_change_request(self, channel, width, height, *args):
        self.size = (width, height)
        return True
    def check_channel_shell_request(self, channel):
        self.ready.set()
        return True
def serve(sock):
    transport = paramiko.Transport(sock)
    try:
        transport.add_server_key(changed if (root / "changed").exists() else host)
        server = Server()
        transport.start_server(server=server)
        channel = transport.accept(10)
        if channel is None or not server.ready.wait(10): return
        channel.sendall(b"SSH fixture ready\r\n")
        while data := channel.recv(8192):
            channel.sendall((f"{server.size[0]}x{server.size[1]}\r\n".encode()) if b"size" in data else b"echo:" + data)
    except (paramiko.SSHException, EOFError, OSError): pass
    finally: transport.close()
with socket.socket() as listener:
    listener.bind(("127.0.0.1", 0))
    listener.listen()
    listener.settimeout(.5)
    (root / "fixture.json").write_text(json.dumps({"port": listener.getsockname()[1], "key": str(root / "fixture-key")}))
    print("Loopback SSH fixture ready", flush=True)
    while not (root / "stop").exists():
        try: sock, _ = listener.accept()
        except socket.timeout: continue
        threading.Thread(target=serve, args=(sock,), daemon=True).start()
