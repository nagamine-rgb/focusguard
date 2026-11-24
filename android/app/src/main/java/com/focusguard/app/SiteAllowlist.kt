package com.focusguard.app

/** ホワイトリスト（ブロック中でも開けるサイト）。Windows / Web と同じ規則で判定する */
object SiteAllowlist {

    /** アドレスバーの URL を読めるブラウザと、そのアドレスバーの部品 ID */
    val urlBarIds: Map<String, List<String>> = mapOf(
        "com.android.chrome" to listOf("com.android.chrome:id/url_bar"),
        "com.chrome.beta" to listOf("com.chrome.beta:id/url_bar"),
        "com.chrome.dev" to listOf("com.chrome.dev:id/url_bar"),
        "com.microsoft.emmx" to listOf("com.microsoft.emmx:id/url_bar"),
        "com.brave.browser" to listOf("com.brave.browser:id/url_bar"),
        "com.kiwibrowser.browser" to listOf("com.kiwibrowser.browser:id/url_bar"),
        "com.sec.android.app.sbrowser" to listOf(
            "com.sec.android.app.sbrowser:id/location_bar_edit_text",
            "com.sec.android.app.sbrowser:id/custom_location_bar_edit_text"
        ),
        "org.mozilla.firefox" to listOf(
            "org.mozilla.firefox:id/mozac_browser_toolbar_url_view",
            "org.mozilla.firefox:id/url_bar_title"
        )
    )

    /** 入力を「example.com」の形にそろえる。使えない入力は null */
    fun normalizeSite(raw: String?): String? {
        var s = (raw ?: "").trim().lowercase()
        if (s.isEmpty()) return null
        val scheme = s.indexOf("://")
        if (scheme >= 0) s = s.substring(scheme + 3)
        s = s.split('/', '?', '#')[0]
        val at = s.lastIndexOf('@')
        if (at >= 0) s = s.substring(at + 1)
        s = s.split(':')[0].trimStart('*', '.')
        if (s.startsWith("www.")) s = s.substring(4)
        s = s.trimEnd('.')
        if (s.isEmpty() || s.length > 253) return null
        if (s != "localhost" && !s.contains('.')) return null
        if (!s.all { it.isLetterOrDigit() || it == '-' || it == '.' }) return null
        return s
    }

    fun normalizeList(raw: List<String>?): List<String> {
        val out = mutableListOf<String>()
        for (r in raw ?: emptyList()) {
            val n = normalizeSite(r) ?: continue
            if (n !in out) out.add(n)
        }
        return out
    }

    /** サイトが増えたか（増やすことは「ブロックを弱める」変更） */
    fun addedAnySite(current: GuardState, proposed: GuardState): Boolean {
        val cur = normalizeList(current.allowedSites).toSet()
        return normalizeList(proposed.allowedSites).any { it !in cur }
    }

    /** アドレスバーの文字からホスト名を取り出す。検索語の入力中などは null */
    fun hostFromUrlBar(text: String?): String? {
        val t = (text ?: "").trim()
        if (t.isEmpty() || t.contains(' ')) return null
        return normalizeSite(t)
    }

    fun isAllowed(host: String, sites: List<String>): Boolean =
        sites.any { host == it || host.endsWith(".$it") }
}
