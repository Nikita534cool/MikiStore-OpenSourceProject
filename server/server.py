from flask import Flask, request, send_from_directory, send_file, abort, render_template, redirect
from flask_cors import CORS
import os
from datetime import datetime, timedelta, timezone

app = Flask(__name__, static_folder=None)
CORS(app, extra_headers=['X-Miki-Secret'])

BASE_DIR = os.path.dirname(os.path.abspath(__file__))

@app.route('/')
def home():
    current_tunnel = request.url_root.rstrip('/') 
    
    return render_template('index.html', official_url=current_tunnel)

@app.route('/icon')
def giv_icon():
    icon_path = os.path.join(BASE_DIR, 'MikiStore.png')
    return send_file(icon_path, mimetype='image/png')

@app.route('/banner')
def giv_banner():
    icon_path = os.path.join(BASE_DIR, 'prtSc.png')
    return send_file(icon_path, mimetype='image/png')

@app.route('/robots.txt')
def no_key_robots():
    return send_from_directory(os.getcwd(), 'robots.txt')

@app.route('/favicon.ico')
def favicon():
    return send_file(os.path.join(BASE_DIR, 'MikiStore.png'))

@app.route('/<path:filename>')
def serve_vault(filename):
    file_path = os.path.join(BASE_DIR, filename)
    if os.path.exists(file_path):
        return send_file(file_path)
    else:
        abort(404)

@app.route('/downloadins')
def download_installer():
    installer_path = os.path.join(BASE_DIR, 'MikiStoreInstaller.exe')
    
    if os.path.exists(installer_path):
        try:
            return send_file(installer_path, as_attachment=True)
        except Exception as e:
            return f"Error delivering file: {str(e)}", 500
    else:
        abort(404, description="MikiStoreInstaller.exe not found on server.")

if __name__ == '__main__':
    app.run(host='0.0.0.0', port=8000, threaded=True)
