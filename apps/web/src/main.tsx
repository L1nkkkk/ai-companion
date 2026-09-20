import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import './style.css';

function App() {
  const [connection, setConnection] = useState('检查服务连接…');
  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(() => controller.abort(), 5000);
    fetch('/health/live', { signal: controller.signal })
      .then(async response => {
        if (!response.ok) throw new Error('Service unavailable');
        const body: unknown = await response.json();
        setConnection(typeof body === 'object' && body !== null && 'status' in body && body.status === 'ok'
          ? '基础服务已连接' : '基础服务尚未就绪');
      })
      .catch(() => setConnection('基础服务未连接'))
      .finally(() => window.clearTimeout(timer));
    return () => { controller.abort(); window.clearTimeout(timer); };
  }, []);
  return (
    <main>
      <p className="eyebrow">AI COMPANION · 开发预览</p>
      <h1>让陪伴有声音，也有表情。</h1>
      <p>桌面、直播和手机，共同连接一个角色。</p>
      <p role="status" className="connection">{connection}</p>
      <p className="note">当前已建立项目起点。角色、聊天与语音将在后续任务中接入。</p>
    </main>
  );
}

const root = document.getElementById('root');
if (!root) throw new Error('Root element is missing');
createRoot(root).render(<React.StrictMode><App /></React.StrictMode>);
