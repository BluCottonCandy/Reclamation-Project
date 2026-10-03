import json
import os
import tkinter as tk
from tkinter import simpledialog
from PIL import Image

# Generate meta.json
def generate_meta_json(width, height, directions):
    # Collect all png files in the folder
    png_files = [f for f in os.listdir('.') if f.endswith('.png')]

    # Build the states list
    states = []
    for file in png_files:
        # Check image dimensions
        with Image.open(file) as img:
            img_width, img_height = img.size

        state = {"name": file.replace('.png', '')}
        # Add "directions" only if >1 and the file size does not match the specified size
        if (img_width != width or img_height != height) and directions > 1:
            state["directions"] = directions

        states.append(state)

    # Generate meta payload
    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": "Python generated",
        "size": {
            "x": width,
            "y": height
        },
        "states": states
    }

    # Save JSON
    with open('meta.json', 'w') as json_file:
        json.dump(meta, json_file, indent=4)

# UI setup
def ask_user_input():
    root = tk.Tk()
    root.withdraw()  # Hide the root window

    # Ask for dimensions
    width = simpledialog.askinteger("Input", "Width in pixels (X):", parent=root, minvalue=1)
    height = simpledialog.askinteger("Input", "Height in pixels (Y):", parent=root, minvalue=1)
    directions = simpledialog.askinteger("Input", "Number of directions:", parent=root, minvalue=0)

    # Finish generation
    if width is not None and height is not None and directions is not None:
        generate_meta_json(width, height, directions)

ask_user_input()

