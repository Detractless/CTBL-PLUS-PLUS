/**
 * Generates a .ctbbl file content compatible with Cold Turkey's import format.
 * @param {Object} template - The template data object
 * @returns {string} - JSON string for the .ctbbl file
 */
export function generateCtblContent(template) {
    const listName = template.title || "Imported List";
    const ctbl = {
        [listName]: {
            "enabled": "true",
            "autostart": "none",
            "type": "continuous",
            "timer": "",
            "startTime": "2026,1,1,0,0",
            "pomodoroTime": "",
            "lock": "delay,96,h,false,none,0,0,0,0,0,0",
            "lockUnblock": "true",
            "restartUnblock": "true",
            "break": "none",
            "password": "",
            "randomTextLength": "",
            "window": "",
            "users": "all",
            "web": template.content.websites || [],
            "exceptions": [],
            "apps": template.content.apps || [],
            "schedule": [],
            "customUsers": []
        }
    };

    return JSON.stringify(ctbl, null, 2);
}
