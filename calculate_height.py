# /usr/bin/python3
import argparse

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("pos_file_path", help="Path to a file that contains positions obtained from in-game getpos command.", type=str)

    args = parser.parse_args()

    with open(args.pos_file_path) as file:
        for line in file:
            words = line.split()
            # Targets
            if len(words) > 6:
                height = float(words[3][:-7])
                height -=65
                crunching = False
                if len(words) == 8:
                    print(words[1] + " " + words[2] + " " + str(height) + " " + words[4] + " " + words[5] + " " + words[6] + " True")
                else:
                    print(words[1] + " " + words[2] + " " + str(height) + " " + words[4] + " " + words[5] + " " + words[6] + " False")
            
            # Joints of guiding line
            if len(words) == 4:
                height = float(words[3])
                height -=55
                print(words[1] + " " + words[2] + " " + str(height))