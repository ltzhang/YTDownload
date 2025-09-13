let currentVideoInfo = null;

browser.runtime.onMessage.addListener((request, sender, sendResponse) => {
  if (request.action === 'updateVideoInfo') {
    currentVideoInfo = request.videoInfo;
  } else if (request.action === 'getVideoInfo') {
    sendResponse(currentVideoInfo);
  } else if (request.action === 'download') {
    downloadVideo(request.videoId, request.videoTitle)
      .then(result => sendResponse(result))
      .catch(error => sendResponse({ success: false, error: error.message }));
    return true;
  }
});

async function downloadVideo(videoId, videoTitle) {
  try {
    const response = await fetch('http://localhost:5000/api/download', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        videoId: videoId,
        videoTitle: videoTitle,
        quality: '1080p'
      })
    });

    if (!response.ok) {
      const errorText = await response.text();
      throw new Error(`Server error: ${errorText}`);
    }

    const result = await response.json();
    
    if (result.success) {
      browser.notifications.create({
        type: 'basic',
        iconUrl: 'icons/icon-48.png',
        title: 'Download Started',
        message: `Downloading: ${videoTitle}`
      });
    }
    
    return result;
  } catch (error) {
    if (error.message.includes('Failed to fetch')) {
      throw new Error('Cannot connect to download server. Please ensure the server is running on localhost:5000');
    }
    throw error;
  }
}

browser.browserAction.onClicked.addListener(async (tab) => {
  if (!tab.url.includes('youtube.com')) {
    browser.notifications.create({
      type: 'basic',
      iconUrl: 'icons/icon-48.png',
      title: 'Not a YouTube page',
      message: 'Please navigate to a YouTube video to download'
    });
  }
});