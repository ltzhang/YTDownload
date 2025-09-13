function getVideoId() {
  const urlParams = new URLSearchParams(window.location.search);
  return urlParams.get('v');
}

function getVideoTitle() {
  const titleElement = document.querySelector('h1.ytd-video-primary-info-renderer yt-formatted-string, h1.style-scope.ytd-watch-metadata yt-formatted-string');
  return titleElement ? titleElement.textContent.trim() : 'YouTube Video';
}

function updateVideoInfo() {
  const videoId = getVideoId();
  if (videoId) {
    const videoTitle = getVideoTitle();
    browser.runtime.sendMessage({
      action: 'updateVideoInfo',
      videoInfo: {
        id: videoId,
        title: videoTitle,
        url: window.location.href
      }
    });
  }
}

const observer = new MutationObserver(() => {
  updateVideoInfo();
});

observer.observe(document.body, {
  childList: true,
  subtree: true
});

updateVideoInfo();

document.addEventListener('yt-navigate-finish', updateVideoInfo);
window.addEventListener('load', updateVideoInfo);