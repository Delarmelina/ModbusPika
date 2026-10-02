from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parent
numbers = list(range(27,44)) + list(range(56,86))
for offset in range(0,len(numbers),4):
    group = numbers[offset:offset+4]
    sheet = Image.new('RGB',(1600,900),'white')
    for index,number in enumerate(group):
        with Image.open(root / 'renders-ampliada' / f'slide-{number:02}.png') as pic:
            pic.thumbnail((800,450))
            sheet.paste(pic,(index%2*800,index//2*450))
    sheet.save(root / f'review-ampliado-{offset//4+1:02}.png')
print('Review sheets generated')
