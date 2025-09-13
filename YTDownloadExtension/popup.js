document.addEventListener('DOMContentLoaded', async () => {
  const noVideoDiv = document.getElementById('noVideo');
  const videoInfoDiv = document.getElementById('videoInfo');
  const serverErrorDiv = document.getElementById('serverError');
  const videoTitleDiv = document.getElementById('videoTitle');
  const downloadBtn = document.getElementById('downloadBtn');
  const statusDiv = document.getElementById('status');
  const qualitySelect = document.getElementById('qualitySelect');
  const debugInfoDiv = document.getElementById('debugInfo');
  const retryBtn = document.getElementById('retryBtn');

  let currentVideo = null;
  let retryCount = 0;
  const maxRetries = 3;

  async function checkServerConnection(showDebug = false) {
    const startTime = Date.now();
    try {
      if (showDebug) {
        debugInfoDiv.textContent = `Attempting connection to http://localhost:5000/api/health...`;
      }

      const response = await fetch('http://localhost:5000/api/health', {
        method: 'GET',
        mode: 'cors',
        headers: {
          'Accept': 'application/json'
        }
      });

      const elapsed = Date.now() - startTime;

      if (response.ok) {
        const data = await response.json();
        if (showDebug) {
          debugInfoDiv.textContent = `✓ Connected successfully (${elapsed}ms) - Server status: ${data.status}`;
        }
        return true;
      } else {
        if (showDebug) {
          debugInfoDiv.textContent = `✗ Server responded with status: ${response.status} (${elapsed}ms)`;
        }
        return false;
      }
    } catch (error) {
      const elapsed = Date.now() - startTime;
      if (showDebug) {
        debugInfoDiv.textContent = `✗ Connection failed (${elapsed}ms): ${error.message}`;
      }
      console.error('Server connection error:', error);
      return false;
    }
  }

  async function getCurrentTab() {
    const tabs = await browser.tabs.query({ active: true, currentWindow: true });
    return tabs[0];
  }

  async function initializeWithRetry(isRetry = false) {
    const tab = await getCurrentTab();

    if (!tab.url || !tab.url.includes('youtube.com/watch')) {
      noVideoDiv.style.display = 'block';
      videoInfoDiv.style.display = 'none';
      serverErrorDiv.style.display = 'none';
      return;
    }

    if (isRetry) {
      retryCount++;
      debugInfoDiv.textContent = `Retry attempt ${retryCount}/${maxRetries}...`;
    }

    const serverAvailable = await checkServerConnection(true);

    if (!serverAvailable) {
      noVideoDiv.style.display = 'none';
      videoInfoDiv.style.display = 'none';
      serverErrorDiv.style.display = 'block';

      if (retryCount < maxRetries && !isRetry) {
        debugInfoDiv.textContent += '\nAutomatically retrying in 2 seconds...';
        setTimeout(() => initializeWithRetry(true), 2000);
      } else if (retryCount >= maxRetries) {
        debugInfoDiv.textContent += `\nMax retries (${maxRetries}) reached. Click retry to try again.`;
      }
      return;
    }

    const response = await browser.runtime.sendMessage({ action: 'getVideoInfo' });

    if (response && response.id) {
      currentVideo = response;
      videoTitleDiv.textContent = response.title || 'YouTube Video';
      noVideoDiv.style.display = 'none';
      videoInfoDiv.style.display = 'block';
      serverErrorDiv.style.display = 'none';
      retryCount = 0;
    } else {
      noVideoDiv.style.display = 'block';
      videoInfoDiv.style.display = 'none';
      serverErrorDiv.style.display = 'none';
    }
  }

  retryBtn.addEventListener('click', async () => {
    retryBtn.disabled = true;
    retryBtn.textContent = 'Retrying...';
    retryCount = 0;

    await initializeWithRetry(false);

    retryBtn.disabled = false;
    retryBtn.textContent = 'Retry Connection';
  });

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

  await initializeWithRetry(false);
});