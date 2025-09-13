document.addEventListener('DOMContentLoaded', async () => {
  const noVideoDiv = document.getElementById('noVideo');
  const videoInfoDiv = document.getElementById('videoInfo');
  const serverErrorDiv = document.getElementById('serverError');
  const videoTitleDiv = document.getElementById('videoTitle');
  const downloadBtn = document.getElementById('downloadBtn');
  const statusDiv = document.getElementById('status');
  const qualitySelect = document.getElementById('qualitySelect');
  
  let currentVideo = null;
  
  async function checkServerConnection() {
    try {
      const response = await fetch('http://localhost:5000/api/health', {
        method: 'GET',
        mode: 'cors'
      });
      return response.ok;
    } catch (error) {
      return false;
    }
  }
  
  async function getCurrentTab() {
    const tabs = await browser.tabs.query({ active: true, currentWindow: true });
    return tabs[0];
  }
  
  async function initialize() {
    const tab = await getCurrentTab();
    
    if (!tab.url || !tab.url.includes('youtube.com/watch')) {
      noVideoDiv.style.display = 'block';
      videoInfoDiv.style.display = 'none';
      serverErrorDiv.style.display = 'none';
      return;
    }
    
    const serverAvailable = await checkServerConnection();
    if (!serverAvailable) {
      noVideoDiv.style.display = 'none';
      videoInfoDiv.style.display = 'none';
      serverErrorDiv.style.display = 'block';
      return;
    }
    
    const response = await browser.runtime.sendMessage({ action: 'getVideoInfo' });
    
    if (response && response.id) {
      currentVideo = response;
      videoTitleDiv.textContent = response.title || 'YouTube Video';
      noVideoDiv.style.display = 'none';
      videoInfoDiv.style.display = 'block';
      serverErrorDiv.style.display = 'none';
    } else {
      noVideoDiv.style.display = 'block';
      videoInfoDiv.style.display = 'none';
      serverErrorDiv.style.display = 'none';
    }
  }
  
  downloadBtn.addEventListener('click', async () => {
    if (!currentVideo) return;
    
    downloadBtn.disabled = true;
    statusDiv.className = 'status loading';
    statusDiv.textContent = 'Starting download...';
    statusDiv.style.display = 'block';
    
    try {
      const result = await browser.runtime.sendMessage({
        action: 'download',
        videoId: currentVideo.id,
        videoTitle: currentVideo.title,
        quality: qualitySelect.value
      });
      
      if (result.success) {
        statusDiv.className = 'status success';
        statusDiv.textContent = 'Download started successfully!';
        setTimeout(() => {
          window.close();
        }, 2000);
      } else {
        statusDiv.className = 'status error';
        statusDiv.textContent = result.error || 'Download failed';
      }
    } catch (error) {
      statusDiv.className = 'status error';
      statusDiv.textContent = 'Error: ' + error.message;
    } finally {
      downloadBtn.disabled = false;
    }
  });
  
  await initialize();
});