JSON.stringify(document.elementsFromPoint(innerWidth-200,200).map(x=>({tag:x.tagName,cls:x.className,bg:getComputedStyle(x).backgroundColor,opacity:getComputedStyle(x).opacity})))
