import path from "node:path";
import fs from "node:fs";
import childProcess from "node:child_process";
import { fileURLToPath } from "node:url";
import { createRequire } from "node:module";
import preGyp from "@mapbox/node-pre-gyp";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

// windows url helper
const urlHelperPath = path.join(
	__dirname,
	"..",
	"bin",
	"windows",
	"UrlFromWindow.exe"
);

const getUrlForHwnd = async (hwnd) => {
	if (!hwnd || !fs.existsSync(urlHelperPath)) {
		return null;
	}

	return new Promise((resolve) => {
		try {
			const child = childProcess.spawn(urlHelperPath, [String(hwnd)], {
				windowsHide: true,
			});
			let out = "";
			child.stdout.on("data", (d) => {
				out += d.toString();
			});
			child.on("close", () => resolve(out.trim() || null));
			child.on("error", () => resolve(null));
		} catch {
			resolve(null);
		}
	});
};

const getUrlForHwndSync = (hwnd) => {
	if (!hwnd || !fs.existsSync(urlHelperPath)) {
		return null;
	}

	try {
		const out = childProcess.execFileSync(urlHelperPath, [String(hwnd)], {
			encoding: "utf8",
			windowsHide: true,
		});
		return (out || "").trim() || null;
	} catch {
		return null;
	}
};

const getAddon = () => {
	const require = createRequire(import.meta.url);

	const bindingPath = preGyp.find(
		path.resolve(path.join(__dirname, "../package.json"))
	);

	return fs.existsSync(bindingPath)
		? require(bindingPath)
		: {
				getActiveWindow() {},
				getOpenWindows() {},
		  };
};

export async function activeWindow() {
	const result = await getAddon().getActiveWindow();
	if (
		result &&
		result.owner &&
		result.id
		// (process.platform === "win32" || process.platform === "windows")
	) {
		const name = (result.owner.name || "").toLowerCase();
		if (
			name.includes("chrome") ||
			name.includes("edge") ||
			name.includes("firefox")
		) {
			const url = await getUrlForHwnd(result.id);
			if (url) {
				result.url = url;
			}
		}
	}
	return result;
}

export function activeWindowSync() {
	const result = getAddon().getActiveWindow();
	if (
		result &&
		result.owner &&
		result.id &&
		(process.platform === "win32" || process.platform === "windows")
	) {
		const name = (result.owner.name || "").toLowerCase();
		if (
			name.includes("chrome") ||
			name.includes("edge") ||
			name.includes("firefox")
		) {
			const url = getUrlForHwndSync(result.id);
			if (url) {
				result.url = url;
			}
		}
	}
	return result;
}

export async function openWindows() {
	const list = await getAddon().getOpenWindows();
	if (
		Array.isArray(list) &&
		list.length > 0 &&
		(process.platform === "win32" || process.platform === "windows")
	) {
		const first = list[0];
		if (first && first.owner && first.id) {
			const name = (first.owner.name || "").toLowerCase();
			if (
				name.includes("chrome") ||
				name.includes("edge") ||
				name.includes("firefox")
			) {
				const url = await getUrlForHwnd(first.id);
				if (url) {
					first.url = url;
				}
			}
		}
	}
	return list;
}

export function openWindowsSync() {
	return getAddon().getOpenWindows();
}
